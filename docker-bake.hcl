variable "REGISTRY" {
  default = ""
}

variable "TAG" {
  default = "local"
}

variable "PACKAGE_CACHE_BUST" {
  default = "unset"
}

function "tag" {
  params = [name]
  result = REGISTRY != "" ? ["${REGISTRY}/${name}:${TAG}"] : ["${name}:${TAG}"]
}

group "default" {
  targets = ["api", "worker", "web", "migrations"]
}

target "_common" {
  context   = "."
  platforms = ["linux/amd64"]
}

target "api" {
  inherits   = ["_common"]
  dockerfile = "backend/Dockerfile"
  tags       = tag("waterflex-api")
  cache-from = ["type=gha,scope=waterflex-api"]
  cache-to   = ["type=gha,mode=max,scope=waterflex-api"]
}

target "worker" {
  inherits   = ["_common"]
  dockerfile = "backend/Dockerfile.worker"
  tags       = tag("waterflex-worker")
  cache-from = ["type=gha,scope=waterflex-worker"]
  cache-to   = ["type=gha,mode=max,scope=waterflex-worker"]
}

target "web" {
  inherits   = ["_common"]
  dockerfile = "web/Dockerfile"
  tags       = tag("waterflex-web")
  args = {
    PACKAGE_CACHE_BUST = PACKAGE_CACHE_BUST
  }
  cache-from = ["type=gha,scope=waterflex-web"]
  cache-to   = ["type=gha,mode=max,scope=waterflex-web"]
}

target "migrations" {
  inherits   = ["_common"]
  dockerfile = "backend/Dockerfile.migrations"
  tags       = tag("waterflex-migrations")
  cache-from = ["type=gha,scope=waterflex-migrations"]
  cache-to   = ["type=gha,mode=max,scope=waterflex-migrations"]
}
