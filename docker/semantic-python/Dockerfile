# codescan/semantic-python — Python semantic analyzer (Phase 2-C stub).
# Contract: stdout NDJSON. Real matcher will use libcst + jedi.

FROM python:3.12-alpine
LABEL org.opencontainers.image.title="codescan-semantic-python"
LABEL org.opencontainers.image.description="Python semantic analyzer (Phase 2-C stub) — libcst + jedi planned"

WORKDIR /app
COPY analyzer.py ./

# Phase 2-C will add: RUN pip install --no-cache-dir libcst jedi

WORKDIR /work
ENTRYPOINT ["python", "/app/analyzer.py"]
