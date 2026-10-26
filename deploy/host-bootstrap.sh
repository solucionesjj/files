#!/bin/bash
# Files single-host bootstrap.
#
# Run ONCE on the existing Ubuntu 24.04 LTS host, over SSH, as a sudo-capable user:
#   ssh -i <key>.pem ubuntu@<public-ip> 'bash -s' < deploy/host-bootstrap.sh
#
# This installs the runtime only. It does NOT configure the firewall (the host's
# firewall/security-group is managed externally and is already locked down), and it
# does NOT clone the application or write any secret. Application deployment is
# documented in deploy/README.md.
set -euxo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root (sudo)." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends \
  ca-certificates curl gnupg unzip git rsync \
  docker.io \
  unattended-upgrades chrony awscli

# Compose v2 is usually packaged; fall back to the upstream plugin binary.
apt-get install -y --no-install-recommends docker-compose-v2 || true
systemctl enable --now docker
if ! docker compose version >/dev/null 2>&1; then
  mkdir -p /usr/local/lib/docker/cli-plugins
  curl -fsSL "https://github.com/docker/compose/releases/download/v2.30.3/docker-compose-linux-x86_64" \
    -o /usr/local/lib/docker/cli-plugins/docker-compose
  chmod 0755 /usr/local/lib/docker/cli-plugins/docker-compose
fi
usermod -aG docker ubuntu || true

# Files directories on the root volume. The firewall is intentionally left untouched.
install -d -m 0750 -o ubuntu -g ubuntu /opt/files
install -d -m 0750 -o ubuntu -g ubuntu /var/lib/files
install -d -m 0750 -o ubuntu -g ubuntu /var/lib/files/staging
install -d -m 0750 -o ubuntu -g ubuntu /var/lib/files/postgres
install -d -m 0755 -o ubuntu -g ubuntu /var/lib/files/portal

# 2 GiB swap to absorb ClamAV + PostgreSQL + API/worker spikes.
if [ ! -f /swapfile ]; then
  fallocate -l 2G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile
  swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
echo 'vm.swappiness=10' > /etc/sysctl.d/99-files.conf
sysctl --system

systemctl enable --now chrony
systemctl enable --now unattended-upgrades
timedatectl set-timezone UTC

echo "Files host bootstrap complete" > /var/log/files-bootstrap.log
