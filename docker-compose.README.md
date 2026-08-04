# Docker deployment notes

## Local stack

Run the stack from the repository root:

```powershell
docker compose up --build
```

- The web app is available at http://localhost:3000.
- The API is available at http://localhost:5188.
- The database is exposed on localhost:5432 for local inspection.

## Staging: build locally, deploy remotely

Staging images are built on the Windows workstation and pushed to private Amazon ECR repositories. The EC2 host
only pulls and starts those images; it does not build source code.

From PowerShell at the repository root, authenticate the AWS CLI and publish a versioned set of images:

```powershell
aws sts get-caller-identity
.\backend\tools\build-and-push-staging-images.ps1
```

The script creates the `waterflex-api`, `waterflex-web`, and `waterflex-worker` ECR repositories when necessary,
builds `linux/amd64` images, tags them with the current Git commit, and pushes them. Its final output contains the
two values required in `/etc/waterflex/deployment.env` on EC2:

```text
ECR_REGISTRY=<aws-account-id>.dkr.ecr.us-east-2.amazonaws.com
IMAGE_TAG=<git-commit>
```

After updating that file on EC2, deploy the selected image set:

```bash
sudo systemctl restart waterflex-api.service
sudo systemctl status waterflex-api.service --no-pager
docker compose -f /home/ubuntu/waterflex-salt/docker-compose.staging.yml ps
```

The staging Compose file intentionally has no `build:` sections. The EC2 startup script authenticates to ECR,
pulls the selected tag, and starts the API, web, and worker containers with `--no-build`.
