# DOC: Sol Bot -- How The Security Is Layered

v1 | Oct 05 2026 - 02:02 PM (MDT)

**Status:** Draft. A layer is marked Running only where it was confirmed in the code. Everything else is marked Designed, even where part of it exists.

---

## What Sol Bot Is

Sol Bot is a pair of automated trading bots that run for a client on a public blockchain. One bot trades when the trend is long, the other when it is short. Each one borrows against collateral on a lending protocol, so a mistake costs real money and can cost it quickly.

The trading logic is private and is not described here. This document is about the other half of the system: everything built so that a bug, a stolen credential, a dead process, or a compromised third party cannot empty the account.

## The Idea

No single layer is trusted. Each one assumes the layer inside it has already failed and asks what the damage would be. The layers below run from the signing key outward to the protocol the money sits in.

## The Layers

| Layer | What it stops | Status |
|-------|---------------|--------|
| Key custody | The signing key being copied off a machine or stolen by a poisoned dependency | Running |
| Signer policy | A valid credential being used to sign something the bot should never sign | Designed |
| Two-wallet money model | A compromise of the trading side reaching the capital | Designed |
| Separation of duties | One compromised component forging a trade by itself | Designed |
| Transaction defenses | Slippage, front-running, and stale or replayed transactions | Designed |
| Position cap | A bot opening more than it is allowed to | Running |
| Risk watcher | An open position riding through a stop, a liquidation line, or a borrow-rate spike | Running |
| Liveness | A silent engine or a silent watcher going unnoticed | Running |
| Flip double-check | The trend flipping and a bot not acting on it | Proof of concept, this repo |
| Protocol guardian | A takeover of the lending protocol's own governance | Designed |

## Key Custody

The bot never holds key bytes. A plaintext key on disk or in an environment variable is forbidden outright. Keys live in a policy signer, Turnkey, which keeps them in secure hardware; the bot holds only an API credential that asks the signer to sign.

Each signing organization has two identities. The root identity can administer the organization and create wallets. The bot signs as a separate, non-root identity, and every request it makes passes through the policy engine. The signing path fails closed: if the non-root identity is missing, nothing is signed. It does not fall back to the root identity.

Preview and production are separate organizations, and so are the operator's and the client's. One policy is defined in code, and a read-back check compares what each organization holds against that definition.

## Signer Policy

The policy is where a stolen credential stops being useful. The design allows the bot identity to sign only for an allowlist of programs, to send funds only to the organization's own main wallet, and only under amount and frequency caps. It rejects instruction shapes that have no place in a trade, such as reassigning an account's owner. The signer is meant to check what a transaction does, not sign a blob it is handed.

## Two-Wallet Money Model

Capital sits in a multisignature wallet that no single key can move. The bots trade from a second wallet that holds a bounded float. Funds move between the two on a one-way rail. If everything on the trading side is compromised at once, the loss is the float.

## Separation Of Duties

The component that decides to trade and the component that signs are separate. An authorization to sign is single-use and bound to the content of one transaction, so it cannot be replayed or pointed at a different transaction. Forging a trade means compromising both sides.

## Transaction Defenses

A transaction is rebuilt at the moment it is signed; nothing long-lived sits pre-signed. Simulation is used as a filter and never as proof. The slippage limit is enforced on-chain and fails closed: when the limit is hit the bot waits and re-quotes. A transaction that moves funds is never retried blindly.

## Position Cap, Risk Watcher, And Liveness

These run today.

The engine carries a cap on what a bot may deposit into one position.

A risk watcher runs as its own process, apart from the engine. It follows every open position against three exits: a price stop sized to the deposit, a loan-to-value guard set ahead of the lender's own liquidation line, and a borrow-rate check that closes a position whose carry has stayed negative for a full dwell window. The engine refuses to arm unless a live, armed watcher answers.

The engine and the watcher write heartbeats and watch each other for silence. A separate dead-man's switch on a different machine probes the engine from outside and pages a person when it stops answering.

## Flip Double-Check

The one failure the layers above do not catch is the quiet one: the trend flips and a bot does not act. Nothing is down, nothing is over a line, and the position is simply wrong.

This repository is the proof of concept for that check, written in C# on ASP.NET Core. It reads the trend and each bot's state, gives the bots a grace window after a flip, and then raises an alert for a bot that should have closed and did not, or should have opened and did not. It only reads. It cannot place, change, or close a trade.

It runs against a scripted market today. Reading the live system needs a read-only feed that the engine does not expose yet.

## Protocol Guardian

The money sits in a lending protocol that can be upgraded by a governance multisignature the depositor does not control. If those governance keys are taken over, every layer above is beside the point.

The guardian is the hedge. A watcher with no keys compares the protocol's on-chain governance state against a pinned baseline: who can upgrade it, the program's hash, the multisignature's members and threshold, and its timelock. A second process, which does hold a key, can do exactly one thing: unwind every position and send the funds to a fixed cold wallet. The two are joined by a signed local trigger with three stages: watch, arm, fire.

This is a hedge and not a guarantee. What it buys is time, and how much depends on the protocol's timelock. With no timelock, a drain can land in one block with no warning.

## What Is Not Here

The trading signal, the strategy, addresses, organization and policy identifiers, and hostnames are left out on purpose.

## Change Log

| Date | Version | Change |
|------|---------|--------|
| Oct 05 2026 | v1 | First draft. |
