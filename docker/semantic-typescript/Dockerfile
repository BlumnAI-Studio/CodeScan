# codescan/semantic-typescript — TypeScript Compiler API semantic analyzer.
# Phase 2-A. Contract: stdout NDJSON, /work read-only mount.

FROM node:22-alpine
LABEL org.opencontainers.image.title="codescan-semantic-typescript"
LABEL org.opencontainers.image.description="TypeScript Compiler API semantic analyzer for codescan (Phase 2-A)"

WORKDIR /app
COPY package.json ./
RUN npm install --omit=dev --no-audit --no-fund

COPY index.js ./

WORKDIR /work
ENTRYPOINT ["node", "/app/index.js"]
