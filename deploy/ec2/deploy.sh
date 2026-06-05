#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="${APP_ROOT:-/opt/scada}"
REPO_DIR="${REPO_DIR:-$PWD}"
ENV_FILE="${ENV_FILE:-/etc/scada/scada.env}"
PUBLIC_ORIGIN="${PUBLIC_ORIGIN:-http://localhost}"
BACKUP_DIR="${BACKUP_DIR:-/opt/scada/backups/$(date -u +%Y%m%d-%H%M%S)}"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Missing $ENV_FILE. Create it from .env.production.example before deploying." >&2
  exit 1
fi

wait_for_url() {
  local name="$1"
  local url="$2"
  local attempts="${3:-30}"
  local delay_seconds="${4:-2}"

  for attempt in $(seq 1 "$attempts"); do
    if curl -fsS "$url" >/dev/null; then
      echo "$name is ready."
      return 0
    fi

    echo "Waiting for $name at $url ($attempt/$attempts)..."
    sleep "$delay_seconds"
  done

  echo "$name did not become ready at $url." >&2
  return 1
}

sudo useradd --system --create-home --shell /usr/sbin/nologin scada 2>/dev/null || true
sudo mkdir -p "$APP_ROOT/backend" "$APP_ROOT/frontend" /etc/scada /etc/scada/certs/mqtt
sudo chown -R scada:scada /etc/scada/certs
sudo chmod 750 /etc/scada/certs /etc/scada/certs/mqtt

echo "Publishing backend..."
dotnet restore "$REPO_DIR/backend/Scada.Api/Scada.Api.csproj"
dotnet publish "$REPO_DIR/backend/Scada.Api/Scada.Api.csproj" -c Release -o "$APP_ROOT/backend"
sudo chown -R scada:scada "$APP_ROOT/backend"

echo "Building frontend..."
pushd "$REPO_DIR/frontend" >/dev/null
npm ci
NEXT_PUBLIC_API_URL= NEXT_PUBLIC_HUB_URL= npm run build
popd >/dev/null

sudo rsync -a --delete \
  "$REPO_DIR/frontend/.next" \
  "$REPO_DIR/frontend/public" \
  "$REPO_DIR/frontend/package.json" \
  "$REPO_DIR/frontend/package-lock.json" \
  "$REPO_DIR/frontend/next.config.js" \
  "$APP_ROOT/frontend/"

pushd "$APP_ROOT/frontend" >/dev/null
sudo npm ci --omit=dev
popd >/dev/null
sudo chown -R scada:scada "$APP_ROOT/frontend"

echo "Installing systemd units and Nginx site..."
sudo mkdir -p "$BACKUP_DIR"
for file in \
  /etc/systemd/system/scada-api.service \
  /etc/systemd/system/scada-frontend.service \
  /etc/nginx/sites-available/scada
do
  if [[ -f "$file" ]]; then
    sudo cp "$file" "$BACKUP_DIR/$(basename "$file")"
  fi
done
echo "Backup created at $BACKUP_DIR"

sudo cp "$REPO_DIR/deploy/ec2/scada-api.service" /etc/systemd/system/scada-api.service
sudo cp "$REPO_DIR/deploy/ec2/scada-frontend.service" /etc/systemd/system/scada-frontend.service
sudo cp "$REPO_DIR/deploy/ec2/nginx-scada.conf" /etc/nginx/sites-available/scada
sudo ln -sfn /etc/nginx/sites-available/scada /etc/nginx/sites-enabled/scada
sudo rm -f /etc/nginx/sites-enabled/default

sudo systemctl daemon-reload
sudo systemctl enable scada-api scada-frontend
sudo systemctl restart scada-api
sudo systemctl restart scada-frontend
if ! sudo nginx -t; then
  echo "Nginx validation failed. Restoring previous Nginx site from $BACKUP_DIR." >&2
  if [[ -f "$BACKUP_DIR/scada" ]]; then
    sudo cp "$BACKUP_DIR/scada" /etc/nginx/sites-available/scada
    sudo nginx -t && sudo systemctl reload nginx
  fi
  exit 1
fi
sudo systemctl reload nginx

echo "Checking deployed services..."
sudo systemctl is-active --quiet scada-api
sudo systemctl is-active --quiet scada-frontend
sudo systemctl is-active --quiet nginx

echo "Checking local API and frontend..."
if ! wait_for_url "API" "http://127.0.0.1:5000/api/health/industrial" 45 2; then
  sudo journalctl -u scada-api -n 80 --no-pager
  exit 1
fi

if ! wait_for_url "frontend" "http://127.0.0.1:3000/weintek-browser" 45 2; then
  sudo journalctl -u scada-frontend -n 80 --no-pager
  exit 1
fi

if [[ "${RUN_WEINTEK_SMOKE:-0}" == "1" ]]; then
  echo "Running optional Weintek ingest smoke test..."
  curl -fsS \
    -X POST "http://127.0.0.1:5000/api/weintek/ingest" \
    -H "Content-Type: application/json" \
    --data '{"gateway":"FHDX_CI","cost_center":"CI","machine":"DEPLOY","timestamp":"DEPLOY_SMOKE","tags":{"CI_DEPLOY_SMOKE":1}}' \
    >/dev/null
fi

echo "Deploy finished."
echo "Open: $PUBLIC_ORIGIN"
