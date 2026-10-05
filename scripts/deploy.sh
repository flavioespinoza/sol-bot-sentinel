#!/usr/bin/env bash
#
# Deploy the sentinel to Cloud Run. Run it again to redeploy; every step is safe to repeat.
#
#   GCP_PROJECT=... FEED_URL=... ./scripts/deploy.sh
#
# What it sets up, and why:
#   - A service account of its own that can read exactly the secrets it needs and nothing else.
#   - CPU always on and exactly one instance. The watchers are background loops, so a service
#     that only gets CPU during a request would stop watching, and two instances would page twice.
#   - Outbound traffic to private addresses goes through the VPC, so the feed is read over the
#     private network, never the public internet. FEED_URL should be the engine's internal address.
#   - Secrets come from Secret Manager at start. None is written here, in a file, or in the image.
#
# Nothing in this script is a project id, an address, or a key. Everything comes from the caller.
#
set -euo pipefail

PROJECT="${GCP_PROJECT:?set GCP_PROJECT to the Google Cloud project id}"
FEED_URL="${FEED_URL:?set FEED_URL to the address of the engine feed}"
REGION="${GCP_REGION:-us-central1}"
NETWORK="${GCP_NETWORK:-default}"
SUBNET="${GCP_SUBNET:-default}"
SERVICE="${SERVICE:-sol-bot-sentinel}"
POLL_MS="${POLL_MS:-15000}"
FLIP_GRACE_MS="${FLIP_GRACE_MS:-300000}"

# Secret names in Secret Manager. The values are never read by this script.
FEED_TOKEN_SECRET="${FEED_TOKEN_SECRET:?set FEED_TOKEN_SECRET to the name of the feed token secret}"
API_TOKEN_SECRET="${API_TOKEN_SECRET:?set API_TOKEN_SECRET to the name of the api token secret}"
PUSHOVER_TOKEN_SECRET="${PUSHOVER_TOKEN_SECRET:-}"
PUSHOVER_USER_SECRET="${PUSHOVER_USER_SECRET:-}"

SA="${SERVICE}"
SA_EMAIL="${SA}@${PROJECT}.iam.gserviceaccount.com"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

say() { printf '\n=== %s\n' "$*"; }
has() { "$@" >/dev/null 2>&1; }

say "project ${PROJECT} | region ${REGION} | service ${SERVICE}"

say "APIs"
gcloud services enable run.googleapis.com cloudbuild.googleapis.com \
	artifactregistry.googleapis.com secretmanager.googleapis.com --project "${PROJECT}"

say "service account"
if has gcloud iam service-accounts describe "${SA_EMAIL}" --project "${PROJECT}"; then
	echo "exists: ${SA_EMAIL}"
else
	gcloud iam service-accounts create "${SA}" --project "${PROJECT}" \
		--display-name "Sol Bot Sentinel"
fi

say "secret access (per secret, read only)"
SECRETS="Watch__FeedToken=${FEED_TOKEN_SECRET}:latest,Watch__ApiToken=${API_TOKEN_SECRET}:latest"
NAMES=("${FEED_TOKEN_SECRET}" "${API_TOKEN_SECRET}")
if [ -n "${PUSHOVER_TOKEN_SECRET}" ] && [ -n "${PUSHOVER_USER_SECRET}" ]; then
	SECRETS="${SECRETS},Watch__PushoverToken=${PUSHOVER_TOKEN_SECRET}:latest,Watch__PushoverUser=${PUSHOVER_USER_SECRET}:latest"
	NAMES+=("${PUSHOVER_TOKEN_SECRET}" "${PUSHOVER_USER_SECRET}")
else
	echo "no Pushover secrets named: alerts will be logged and served, and nobody will be paged"
fi
for NAME in "${NAMES[@]}"; do
	gcloud secrets describe "${NAME}" --project "${PROJECT}" >/dev/null || {
		echo "FATAL: secret ${NAME} does not exist in ${PROJECT}" >&2
		exit 1
	}
	gcloud secrets add-iam-policy-binding "${NAME}" --project "${PROJECT}" \
		--member "serviceAccount:${SA_EMAIL}" --role roles/secretmanager.secretAccessor \
		--condition=None --quiet >/dev/null
	echo "bound: ${NAME}"
done

say "deploy"
gcloud run deploy "${SERVICE}" \
	--project "${PROJECT}" --region "${REGION}" \
	--source "${REPO_ROOT}" \
	--service-account "${SA_EMAIL}" \
	--no-cpu-throttling --min-instances 1 --max-instances 1 \
	--network "${NETWORK}" --subnet "${SUBNET}" --vpc-egress private-ranges-only \
	--allow-unauthenticated \
	--set-env-vars "Watch__Source=http,Watch__FeedUrl=${FEED_URL},Watch__PollMs=${POLL_MS},Watch__FlipGraceMs=${FLIP_GRACE_MS}" \
	--set-secrets "${SECRETS}" \
	--quiet

say "health"
URL="$(gcloud run services describe "${SERVICE}" --project "${PROJECT}" --region "${REGION}" \
	--format 'value(status.url)')"
for _ in $(seq 1 30); do
	HEALTH="$(curl -sf --max-time 5 "${URL}/health" || true)"
	case "${HEALTH}" in *'"status":"ok"'*) break ;; esac
	sleep 4
done
echo "${URL}/health"
echo "${HEALTH}"
case "${HEALTH}" in
	*'"status":"ok"'*'"source":"http"'*) echo "OK: watching the live feed" ;;
	*) echo "FATAL: the service did not report ok on the live feed" >&2; exit 1 ;;
esac
