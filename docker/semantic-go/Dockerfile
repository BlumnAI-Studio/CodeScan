# codescan/semantic-go — Go semantic analyzer (Phase 2-B stub).
# Contract: stdout NDJSON. Real matcher will use go/packages + go/types.

# -------- build stage --------
FROM golang:1.22-alpine AS build
WORKDIR /src
COPY go.mod ./
COPY main.go ./
RUN go build -o /out/semantic-go ./...

# -------- runtime stage --------
FROM alpine:3.20
LABEL org.opencontainers.image.title="codescan-semantic-go"
LABEL org.opencontainers.image.description="Go semantic analyzer (Phase 2-B stub) — go/packages planned"

COPY --from=build /out/semantic-go /app/semantic-go

WORKDIR /work
ENTRYPOINT ["/app/semantic-go"]
