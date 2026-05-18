# codescan/semantic-kotlin — Kotlin semantic analyzer (Phase 1-B stub).
# Contract: stdout NDJSON. Real matcher uses Kotlin Analysis API +
# Pekko Typed matchers (Behavior<T>, context.spawn).

FROM eclipse-temurin:21-jre-alpine
LABEL org.opencontainers.image.title="codescan-semantic-kotlin"
LABEL org.opencontainers.image.description="Kotlin semantic analyzer (Phase 1-B stub) — Pekko Typed matchers planned"

WORKDIR /app
COPY analyzer.sh ./
RUN chmod +x analyzer.sh

# Phase 1-B will add the Kotlin compiler/analysis API:
#   RUN apk add --no-cache curl unzip \
#       && curl -fsSL https://github.com/JetBrains/kotlin/releases/download/v2.0.21/kotlin-compiler-2.0.21.zip -o /tmp/k.zip \
#       && unzip -q /tmp/k.zip -d /opt && rm /tmp/k.zip

WORKDIR /work
ENTRYPOINT ["/app/analyzer.sh"]
