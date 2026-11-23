# BookFlow — DEPLOYMENT.md

Target: one Linux VPS (2 vCPU / 4 GB RAM is enough for the pilot) running Docker Compose. Everything below is relative to the repository root.

## 1. Prerequisites

* Ubuntu 22.04/24.04 VPS with Docker Engine 24+ and the Compose plugin (`docker compose version`).
* A DNS `A` record for your domain (e.g. `app.yourbookstore.in`) pointing at the VPS.
* Ports 80 and 443 open. Nothing else needs to be exposed.

## 2. First deployment

```bash
git clone <your-repo> bookflow && cd bookflow
cp deploy/.env.example deploy/.env
nano deploy/.env            # set SA_PASSWORD, JWT_KEY (openssl rand -base64 48), DOMAIN, ACME_EMAIL, PUBLIC_URL=https://<DOMAIN>
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build
```

What happens: SQL Server starts and passes its health check → the `migrate` job applies EF Core migrations and exits → the API starts and passes `/health` → nginx serves the Angular app → Caddy obtains a Let's Encrypt certificate for `DOMAIN` and proxies to nginx. The `backup` sidecar switches the database to FULL recovery and starts the backup schedule.

Verify:

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml ps
curl -fsS https://<DOMAIN>/health && curl -fsS https://<DOMAIN>/api/v1/../health/ready
```

Open `https://<DOMAIN>`, click **Register your business**, create the first bookstore (the registering user becomes Owner). There is no global super-admin UI in v1; every tenant self-registers.

## 3. Local development (without Docker for the apps)

```bash
# database only
docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Your_password123' -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
# API (applies migrations automatically in Development)
cd backend && dotnet run --project src/BookFlow.API        # http://localhost:5000/swagger
# SPA
cd frontend && npm ci && npx ng serve                       # http://localhost:4200 (proxies to localhost:5000 via environment.ts)
```

Creating a migration after changing entities:

```bash
cd backend
dotnet tool install -g dotnet-ef
dotnet ef migrations add <Name> -p src/BookFlow.Infrastructure -s src/BookFlow.API -o Persistence/Migrations
```

## 4. Upgrading

```bash
git pull
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build
```
The `migrate` job runs the new migrations before the new API starts. Take a manual backup first for schema-changing releases: `./deploy/scripts/backup.sh`.

Rollback: `git checkout <previous-tag>` and run the same `up -d --build`. Migrations are forward-only; if a migration must be undone, restore the pre-upgrade backup (§6) and redeploy the previous tag.

## 5. Backups (RPO target 15 min, RTO target 1 h)

* The `backup` sidecar writes a full backup nightly at 01:30 IST and a transaction-log backup every 15 minutes into the `bookflow_backups` Docker volume, pruning files older than `BACKUP_RETENTION_DAYS`.
* **Copy the volume off the server.** Add to the host crontab, e.g. with rclone to S3/Backblaze/Google Drive:

```cron
0 2 * * * docker run --rm -v bookflow_bookflow_backups:/b:ro -v /root/.config/rclone:/config/rclone rclone/rclone sync /b remote:bookflow-backups
```

* Manual full backup any time: `./deploy/scripts/backup.sh`.
* Verify restores quarterly: `./deploy/scripts/restore.sh BookFlow_full_<stamp>.bak BookFlowVerify` restores into a side database without touching production.

## 6. Restore (disaster recovery)

1. Provision a new VPS, install Docker, clone the repo, restore `deploy/.env` from your secrets store.
2. Start only the database: `docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d sqlserver`.
3. Copy the backup files into the volume: `docker cp BookFlow_full_x.bak bookflow-sqlserver-1:/var/opt/mssql/backup/` (same for `.trn` files).
4. `./deploy/scripts/restore.sh BookFlow_full_x.bak` — restores the full backup and applies all later log backups.
5. Start everything: `up -d --build`. Point DNS at the new server.

## 7. Monitoring and logs

* Liveness `GET /health`, readiness `GET /health/ready` (checks SQL Server).
* Structured JSON-ish logs to stdout: `docker compose logs -f api`. Ship them with Loki/Promtail, Datadog, or Azure Monitor agent if needed; every request log line carries method, path, status, elapsed ms and the trace id that also appears in error responses (`traceId`).
* Suggested alerts: `/health/ready` failing for 2 min; 5xx rate > 1 % over 5 min; disk on the backup volume > 80 %; no `log backup` line in `docker compose logs backup` for 1 h.

## 8. Security checklist before go-live

- [ ] `JWT_KEY` and `SA_PASSWORD` are unique, random and stored outside the repo.
- [ ] `SWAGGER_ENABLED=false` in production.
- [ ] `PUBLIC_URL` is exactly `https://<DOMAIN>` (CORS).
- [ ] Optional: create least-privilege logins with `deploy/sql/create-logins.sql` and switch the connection strings.
- [ ] Host firewall allows only 22/80/443; SSH key auth only; unattended-upgrades on.
- [ ] Off-site backup copy verified by a test restore.

## 9. Azure / AWS later

The images are plain OCI images. To move off the VPS: push them (CI already publishes to GHCR), run the API on Azure Container Apps / AWS App Runner, use Azure SQL / RDS for SQL Server, and serve the `web` image or the `dist/` folder from a Static Web App / S3+CloudFront. No code changes are needed; only environment variables.
