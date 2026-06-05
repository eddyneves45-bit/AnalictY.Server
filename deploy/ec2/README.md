# EC2 Deploy

This deploy path runs:

- ASP.NET Core API on `127.0.0.1:5000`
- Next.js frontend on `127.0.0.1:3000`
- Nginx on ports `80` and `443`, redirecting HTTP to HTTPS and proxying `/api` and `/hubs` to the API
- RDS MySQL as the MES database

## 1. EC2 Security Group

Open:

- `22/tcp` only from the authorized DEV/admin IP
- `80/tcp` from the internet
- `443/tcp` from the internet after TLS is configured

Do not expose `5000` or `3000`.

The RDS Security Group should allow database access only from the EC2 application server and explicitly authorized administrative IPs. Do not expose RDS directly to end users.

## 2. Install Base Packages

Ubuntu:

```bash
sudo apt update
sudo apt install -y git nginx rsync curl ca-certificates npm
```

Install .NET 8 SDK using Microsoft packages for your Ubuntu version:

```bash
dotnet --version
```

## 3. Clone And Configure

```bash
git clone https://github.com/eddyneves45-bit/scada_mes.git
cd scada_mes
sudo mkdir -p /etc/scada
sudo cp .env.production.example /etc/scada/scada.env
sudo nano /etc/scada/scada.env
```

Set real values for:

- `Jwt__Key`
- `SeedUsers__AdminPassword`
- `BootstrapMySql__Password`
- `Cors__AllowedOrigins__0`

For first HTTP deploy, `Cors__AllowedOrigins__0` can be:

```text
http://YOUR_EC2_PUBLIC_IP
```

After HTTPS/domain setup, change it to:

```text
https://iiot.analicty.com.br
```

## 4. Deploy

```bash
chmod +x deploy/ec2/deploy.sh
PUBLIC_ORIGIN=http://YOUR_EC2_PUBLIC_IP ./deploy/ec2/deploy.sh
```

## 5. Check Logs

```bash
sudo systemctl status scada-api
sudo systemctl status scada-frontend
sudo journalctl -u scada-api -f
sudo journalctl -u scada-frontend -f
```

## 6. HTTPS

After pointing a domain to the EC2 public IP:

```bash
sudo apt install -y certbot python3-certbot-nginx
sudo certbot --nginx -d iiot.analicty.com.br
```

Then update `/etc/scada/scada.env`:

```text
Cors__AllowedOrigins__0=https://iiot.analicty.com.br
```

Restart:

```bash
sudo systemctl restart scada-api
```

## 7. GitHub Actions CI/CD

The workflow `.github/workflows/deploy-ec2.yml` deploys automatically on every push to `main`.

Recommended production model: install a GitHub Actions self-hosted runner on the EC2 instance. This avoids opening SSH access from GitHub-hosted runners.

In GitHub:

1. Open `Settings > Actions > Runners`.
2. Click `New self-hosted runner`.
3. Choose `Linux x64`.
4. Run the GitHub-provided commands on the EC2 instance.
5. Add the runner label `scada-mes`.

Create this repository variable:

- `PUBLIC_ORIGIN`: public app URL, for example `https://iiot.analicty.com.br`

The EC2 server still keeps runtime secrets in `/etc/scada/scada.env`; they are not copied from GitHub.

Manual deploy:

1. Open `Actions > deploy-ec2`.
2. Click `Run workflow`.
3. Keep `Run optional Weintek ingest smoke test` disabled for normal deploys.
4. Enable it only when you want to send a test payload to `/api/weintek/ingest`.

Post-deploy checks performed by the pipeline:

- `scada-api`, `scada-frontend` and `nginx` are active.
- Local API responds on `/api/health/industrial`.
- Local frontend renders `/weintek-browser`.
- Optional Weintek POST smoke can validate `/api/weintek/ingest`.

Deployment flow:

```text
git push
GitHub Actions verify job
GitHub self-hosted runner on EC2 checks out the repo
deploy/ec2/deploy.sh runs locally on EC2
systemd restarts API/frontend
Nginx serves the new version
```
