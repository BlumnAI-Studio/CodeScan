/** fetch 래퍼 - 텍스트/JSON 로드와 에러 처리.
 *
 *  Cache-busting: GitHub Pages serves static files with long cache headers, and
 *  reverse proxies / browser caches frequently keep stale `indexes/*.json` and
 *  `data/*.json` after a `doc-v*` redeploy. We append a per-page-load timestamp
 *  to every fetch URL so reload always pulls fresh content; inside the same
 *  session the timestamp stays constant so duplicate calls still hit cache.
 */

const PAGE_LOAD_ID = Date.now();

function withBust(url) {
  const sep = url.includes('?') ? '&' : '?';
  return `${url}${sep}_t=${PAGE_LOAD_ID}`;
}

export async function fetchText(url) {
  const res = await fetch(withBust(url));
  if (!res.ok) throw new Error(`fetch ${url} -> ${res.status}`);
  return await res.text();
}

export async function fetchJson(url) {
  const res = await fetch(withBust(url));
  if (!res.ok) throw new Error(`fetch ${url} -> ${res.status}`);
  return await res.json();
}

/** 인덱스 파일 로드 (harness-view/indexes/*.json) */
export async function loadIndex(name) {
  try {
    return await fetchJson(`indexes/${name}.json`);
  } catch (e) {
    console.warn(`[loadIndex] ${name}: ${e.message}`);
    return null;
  }
}

/** 정적 data (사전구현) 로드 (harness-view/data/*.json) */
export async function loadData(name) {
  try {
    return await fetchJson(`data/${name}.json`);
  } catch (e) {
    console.warn(`[loadData] ${name}: ${e.message}`);
    return null;
  }
}

/** Resource (MD) loader.
 *  Pages now uploads the entire repo (artifact path `.`), so the viewer
 *  fetches upstream MDs directly — no mirror, no duplication. From
 *  Home/harness-view/, `../../<rel>` reaches the repo root in both local
 *  dev and Pages runs. */
export async function loadMd(relativePath) {
  try {
    return await fetchText(`../../${relativePath}`);
  } catch (e) {
    console.warn(`[loadMd] ${relativePath}: ${e.message}`);
    return null;
  }
}
