# ADR-0001 One tenant = one bookstore (branch-ready schema)

Status: Accepted · 2026-11-09

## Context
BRD Part 9 asks whether a tenant is a single store or a multi-branch business.

## Decision
A tenant represents one legal business. Every stock and sales table carries a StoreId, and each tenant gets one default Store at registration. UI exposes no store switching in v1.

## Consequences
Multi-branch (P2) becomes adding stores + a store selector + transfer movements (already a MovementType), with no schema migration of fact tables.
