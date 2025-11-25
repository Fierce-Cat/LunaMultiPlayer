# Production Deployment Guide

This guide covers Phase 5 of the Nakama integration - deploying a production-ready, geographically distributed Nakama cluster for LunaMultiplayer.

## Overview

A production deployment requires:
- High availability cluster configuration
- Geographic distribution for low latency
- Monitoring and alerting
- Backup and disaster recovery
- Security hardening
- Performance optimization

## Architecture

### Recommended Production Architecture

```
                                    ┌─────────────────────┐
                                    │    Load Balancer    │
                                    │  (nginx/HAProxy)    │
                                    └──────────┬──────────┘
                                               │
                  ┌────────────────────────────┼────────────────────────────┐
                  │                            │                            │
         ┌────────▼────────┐          ┌────────▼────────┐          ┌────────▼────────┐
         │   Nakama Node   │          │   Nakama Node   │          │   Nakama Node   │
         │   (US-West)     │          │   (US-East)     │          │   (EU)          │
         └────────┬────────┘          └────────┬────────┘          └────────┬────────┘
                  │                            │                            │
                  └────────────────────────────┼────────────────────────────┘
                                               │
                                    ┌──────────▼──────────┐
                                    │    CockroachDB      │
                                    │  (Distributed DB)   │
                                    │  or PostgreSQL HA   │
                                    └─────────────────────┘
```

## Deployment Options

### Option 1: Self-Hosted (VPS/Dedicated Servers)

#### Infrastructure Requirements

| Component | Minimum | Recommended | High-Traffic |
|-----------|---------|-------------|--------------|
| **Nakama Nodes** | 1 | 3 | 5+ |
| **CPU (per node)** | 2 cores | 4 cores | 8+ cores |
| **RAM (per node)** | 4 GB | 8 GB | 16+ GB |
| **Storage** | 50 GB SSD | 100 GB SSD | 250+ GB NVMe |
| **Database** | PostgreSQL | PostgreSQL HA | CockroachDB |
| **Network** | 100 Mbps | 1 Gbps | 10 Gbps |

#### Recommended Providers

- **DigitalOcean**: Good for starting, global regions
- **Vultr**: Cost-effective, good performance
- **Hetzner**: Excellent value in EU
- **AWS/GCP/Azure**: Enterprise-grade, but higher cost

### Option 2: Heroic Cloud (Managed Nakama)

Heroic Labs offers a managed Nakama service:
- Automatic scaling
- Global edge nodes
- Managed updates
- 24/7 support

Best for: Teams that want minimal operational overhead.

Website: https://heroiclabs.com/heroic-cloud/

### Option 3: Kubernetes Deployment

For large-scale deployments, Kubernetes provides:
- Auto-scaling
- Self-healing
- Rolling updates
- Resource efficiency

## Self-Hosted Deployment Guide

### Prerequisites

1. Linux servers (Ubuntu 22.04 LTS recommended)
2. Docker and Docker Compose installed
3. Domain name with DNS configured
4. SSL certificates (Let's Encrypt recommended)

### Step 1: Server Preparation

```bash
# On each Nakama node
# Update system
sudo apt update && sudo apt upgrade -y

# Install Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh

# Install Docker Compose
sudo apt install docker-compose-plugin -y

# Create directories
sudo mkdir -p /opt/nakama/data
sudo mkdir -p /opt/nakama/modules
sudo chown -R $USER:$USER /opt/nakama
```

### Step 2: Database Setup (PostgreSQL HA)

For a single-node development setup:

```yaml
# /opt/nakama/docker-compose.db.yml
version: '3.8'

services:
  postgres:
    image: postgres:15-alpine
    container_name: nakama-postgres
    environment:
      POSTGRES_USER: nakama
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: nakama
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U nakama"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
```

For production HA, consider:
- **CockroachDB**: Distributed, auto-replicating
- **PostgreSQL with Patroni**: HA PostgreSQL cluster
- **Amazon RDS/Aurora**: Managed, multi-AZ

### Step 3: Nakama Cluster Configuration

```yaml
# /opt/nakama/docker-compose.yml
version: '3.8'

services:
  nakama:
    image: heroiclabs/nakama:3.19.0
    container_name: nakama
    entrypoint:
      - "/bin/sh"
      - "-c"
      - |
        /nakama/nakama migrate up --database.address postgres:${POSTGRES_PASSWORD}@postgres:5432/nakama && \
        exec /nakama/nakama \
          --name nakama-${NODE_NAME:-node1} \
          --database.address postgres:${POSTGRES_PASSWORD}@postgres:5432/nakama \
          --socket.server_key ${SERVER_KEY} \
          --console.username ${CONSOLE_USER} \
          --console.password ${CONSOLE_PASSWORD} \
          --runtime.path /nakama/data/modules \
          --session.token_expiry_sec 86400 \
          --socket.max_message_size_bytes 1048576 \
          --match.max_empty_sec 0 \
          --logger.level INFO \
          --logger.stdout \
          --metrics.prometheus_port 9100
    depends_on:
      postgres:
        condition: service_healthy
    ports:
      - "7349:7349"  # gRPC
      - "7350:7350"  # HTTP/WebSocket
      - "7351:7351"  # Console
      - "9100:9100"  # Prometheus metrics
    volumes:
      - ./data/modules:/nakama/data/modules:ro
    restart: unless-stopped
    environment:
      - TZ=UTC

  postgres:
    image: postgres:15-alpine
    container_name: nakama-postgres
    environment:
      POSTGRES_USER: nakama
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: nakama
    volumes:
      - postgres_data:/var/lib/postgresql/data
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U nakama"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
```

### Step 4: Environment Configuration

```bash
# /opt/nakama/.env
POSTGRES_PASSWORD=your_secure_password_here
SERVER_KEY=your_unique_server_key_here
CONSOLE_USER=admin
CONSOLE_PASSWORD=your_secure_console_password
NODE_NAME=us-west-1
```

### Step 5: Load Balancer Configuration (nginx)

```nginx
# /etc/nginx/sites-available/nakama
upstream nakama_http {
    least_conn;
    server nakama-node1:7350 weight=1;
    server nakama-node2:7350 weight=1;
    server nakama-node3:7350 weight=1;
}

upstream nakama_grpc {
    least_conn;
    server nakama-node1:7349 weight=1;
    server nakama-node2:7349 weight=1;
    server nakama-node3:7349 weight=1;
}

# HTTP/WebSocket
server {
    listen 443 ssl http2;
    server_name lmp.yourdomain.com;
    
    ssl_certificate /etc/letsencrypt/live/lmp.yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/lmp.yourdomain.com/privkey.pem;
    
    # SSL configuration
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
    ssl_prefer_server_ciphers on;
    
    # WebSocket support
    location / {
        proxy_pass http://nakama_http;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # WebSocket timeout
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}

# gRPC
server {
    listen 443 ssl http2;
    server_name grpc.lmp.yourdomain.com;
    
    ssl_certificate /etc/letsencrypt/live/lmp.yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/lmp.yourdomain.com/privkey.pem;
    
    location / {
        grpc_pass grpc://nakama_grpc;
        grpc_set_header Host $host;
        grpc_set_header X-Real-IP $remote_addr;
    }
}
```

### Step 6: SSL Certificate Setup

```bash
# Install certbot
sudo apt install certbot python3-certbot-nginx -y

# Obtain certificate
sudo certbot --nginx -d lmp.yourdomain.com -d grpc.lmp.yourdomain.com

# Auto-renewal is configured automatically
```

### Step 7: Deploy LMP Match Handler

```bash
# Copy LMP modules to Nakama
cp /path/to/lmp_match.lua /opt/nakama/data/modules/
cp /path/to/main.lua /opt/nakama/data/modules/

# Restart Nakama to load modules
docker compose restart nakama
```

### Step 8: Start Services

```bash
cd /opt/nakama

# Start all services
docker compose up -d

# Check status
docker compose ps

# View logs
docker compose logs -f nakama
```

## Monitoring & Alerting

### Prometheus Configuration

```yaml
# /opt/prometheus/prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'nakama'
    static_configs:
      - targets:
        - nakama-node1:9100
        - nakama-node2:9100
        - nakama-node3:9100
```

### Grafana Dashboard

Import the official Nakama Grafana dashboard:
- Dashboard ID: `12345` (example, check Heroic Labs docs)

Key metrics to monitor:
- `nakama_session_count` - Active sessions
- `nakama_match_count` - Active matches
- `nakama_message_count` - Messages per second
- `nakama_api_latency` - API response times
- `nakama_storage_ops` - Storage operations

### Alerting Rules

```yaml
# /opt/prometheus/alerts.yml
groups:
  - name: nakama
    rules:
      - alert: NakamaHighLatency
        expr: nakama_api_latency_seconds{quantile="0.99"} > 0.5
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High API latency on {{ $labels.instance }}"
          
      - alert: NakamaDown
        expr: up{job="nakama"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "Nakama instance {{ $labels.instance }} is down"
          
      - alert: NakamaHighSessionCount
        expr: nakama_session_count > 1000
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High session count on {{ $labels.instance }}"
```

## Backup & Recovery

### Database Backup Script

```bash
#!/bin/bash
# /opt/nakama/scripts/backup.sh

BACKUP_DIR="/opt/nakama/backups"
DATE=$(date +%Y%m%d_%H%M%S)
POSTGRES_CONTAINER="nakama-postgres"

# Create backup directory
mkdir -p $BACKUP_DIR

# Backup PostgreSQL
docker exec $POSTGRES_CONTAINER pg_dump -U nakama nakama | gzip > $BACKUP_DIR/nakama_$DATE.sql.gz

# Keep only last 7 days of backups
find $BACKUP_DIR -name "*.sql.gz" -mtime +7 -delete

# Upload to S3 (optional)
# aws s3 cp $BACKUP_DIR/nakama_$DATE.sql.gz s3://your-backup-bucket/nakama/
```

Add to crontab:
```bash
# Daily backup at 3 AM
0 3 * * * /opt/nakama/scripts/backup.sh
```

### Restore Procedure

```bash
# Stop Nakama
docker compose stop nakama

# Restore database
gunzip -c /opt/nakama/backups/nakama_YYYYMMDD_HHMMSS.sql.gz | docker exec -i nakama-postgres psql -U nakama nakama

# Start Nakama
docker compose start nakama
```

## Security Hardening

### Firewall Configuration

```bash
# UFW firewall rules
sudo ufw default deny incoming
sudo ufw default allow outgoing

# SSH (restrict to your IP)
sudo ufw allow from YOUR_IP to any port 22

# Nakama ports (only through load balancer)
# If using internal network
sudo ufw allow from 10.0.0.0/8 to any port 7349
sudo ufw allow from 10.0.0.0/8 to any port 7350

# Enable firewall
sudo ufw enable
```

### Network Security

1. **Use internal networks** for database communication
2. **Enable TLS** for all external connections
3. **Use strong passwords** and rotate regularly
4. **Limit console access** to trusted IPs
5. **Enable rate limiting** in nginx

### Nakama Security Settings

```yaml
# Add to Nakama configuration
socket:
  server_key: "unique_long_random_key_here"
  
session:
  token_expiry_sec: 86400
  refresh_token_expiry_sec: 604800

console:
  # Restrict to internal network or VPN
  address: "10.0.0.1:7351"
```

## Performance Optimization

### Nakama Tuning

```bash
# Nakama runtime flags
--socket.max_message_size_bytes=1048576    # 1MB max message
--socket.read_buffer_size_bytes=4096
--socket.write_buffer_size_bytes=4096
--match.input_queue_size=128
--match.call_queue_size=128
--match.deferred_broadcast_period_ms=50
```

### PostgreSQL Tuning

```ini
# postgresql.conf optimizations for Nakama
shared_buffers = 256MB
effective_cache_size = 768MB
maintenance_work_mem = 64MB
checkpoint_completion_target = 0.9
wal_buffers = 16MB
default_statistics_target = 100
random_page_cost = 1.1
effective_io_concurrency = 200
work_mem = 6553kB
min_wal_size = 1GB
max_wal_size = 4GB
max_worker_processes = 4
max_parallel_workers_per_gather = 2
max_parallel_workers = 4
max_parallel_maintenance_workers = 2
```

### Linux Kernel Tuning

```bash
# /etc/sysctl.d/99-nakama.conf
net.core.somaxconn = 65535
net.core.netdev_max_backlog = 65535
net.ipv4.tcp_max_syn_backlog = 65535
net.ipv4.tcp_fin_timeout = 10
net.ipv4.tcp_tw_reuse = 1
net.core.rmem_max = 16777216
net.core.wmem_max = 16777216
net.ipv4.tcp_rmem = 4096 87380 16777216
net.ipv4.tcp_wmem = 4096 65536 16777216

# Apply changes
sudo sysctl -p /etc/sysctl.d/99-nakama.conf
```

## Geographic Distribution

### Multi-Region Setup

1. **Primary Region (US-West)**
   - Main database (PostgreSQL primary)
   - Nakama node
   - Global coordinator

2. **Secondary Regions (US-East, EU)**
   - Database replica (read-only)
   - Nakama node
   - Local match hosting

### DNS-Based Load Balancing

Use GeoDNS (Route 53, Cloudflare) to route players to nearest region:

```
lmp.yourdomain.com -> 
  US players -> us-west.lmp.yourdomain.com
  EU players -> eu.lmp.yourdomain.com
  Asia players -> asia.lmp.yourdomain.com
```

## Beta Rollout Plan

### Phase 1: Private Beta (Week 1-2)
- Invite 10-20 trusted community members
- Monitor closely for issues
- Collect feedback

### Phase 2: Public Beta (Week 2-4)
- Open to broader community
- Optional opt-in from mod settings
- Lidgren remains default

### Phase 3: General Availability
- Make Nakama the default
- Lidgren as fallback option
- Full production support

## Rollback Plan

If critical issues arise:

1. **Immediate**: Update client config to use Lidgren fallback
2. **Short-term**: Keep Lidgren servers running as backup
3. **Long-term**: Fix issues and gradually re-enable Nakama

## Cost Estimation

### Self-Hosted (Monthly)

| Component | Small | Medium | Large |
|-----------|-------|--------|-------|
| VPS (3 nodes) | $60 | $180 | $480 |
| Database | $20 | $100 | $300 |
| Bandwidth | $10 | $50 | $200 |
| Monitoring | Free | $30 | $100 |
| **Total** | **$90** | **$360** | **$1,080** |

### Heroic Cloud (Monthly)

| Tier | CCU | Price |
|------|-----|-------|
| Starter | 500 | $99 |
| Growth | 5,000 | $499 |
| Enterprise | Unlimited | Custom |

## Checklist

### Pre-Deployment
- [ ] Infrastructure provisioned
- [ ] SSL certificates obtained
- [ ] DNS configured
- [ ] Firewall rules set
- [ ] Monitoring configured
- [ ] Backup system tested

### Deployment
- [ ] Database deployed and migrated
- [ ] Nakama nodes deployed
- [ ] Load balancer configured
- [ ] LMP modules deployed
- [ ] Health checks passing

### Post-Deployment
- [ ] Beta users onboarded
- [ ] Monitoring dashboards reviewed
- [ ] Alerting tested
- [ ] Backup verified
- [ ] Documentation updated

---

**Previous**: [Social Features](./SocialFeatures.md)  
**Main Document**: [README](./README.md)
