# Docker deployment notes

## Local stack

Run the stack from the repository root:

```powershell
docker compose up --build
```

- The web app is available at http://localhost:3000.
- The API is available at http://localhost:5188.
- The database is exposed on localhost:5432 for local inspection.

## Production-oriented note

This initial compose setup is intended for local development and staging validation. For AWS, the same container images can be deployed behind a reverse proxy or load balancer, while PostgreSQL remains on Amazon RDS rather than inside Compose.
