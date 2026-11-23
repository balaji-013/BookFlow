# ADR-0009 JWT bearer tokens with rotating refresh tokens

Status: Accepted

## Decision
Access token 15 min, refresh token 7 days, rotation with reuse detection. Tokens held in memory + localStorage refresh token by the SPA. Chosen over cookie sessions to keep the API stateless and CSRF-free; XSS mitigated by Angular's default escaping and CSP.
