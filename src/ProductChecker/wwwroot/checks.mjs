export function parseUrls(text) {
  const urls = [...new Set(text.split(/\r?\n/).map(line => line.trim()).filter(Boolean))];
  if (!urls.length) throw new Error('Enter at least one product page URL.');
  if (urls.length > 10) throw new Error('Enter no more than 10 different URLs at a time.');
  return urls;
}

export function isProductUrl(value) {
  try {
    const url = new URL(value);
    return value.length <= 4096 && url.protocol === 'https:' && !url.username && !url.password && (!url.port || url.port === '443');
  } catch { return false; }
}

// Each URL keeps its own result, even when another request fails.
export async function checkUrls(urls, onResult, fetchPage = fetch) {
  let next = 0;
  async function worker() {
    while (next < urls.length) {
      const index = next++, url = urls[index];
      let result;
      try {
        if (!isProductUrl(url)) throw new Error('Enter a valid HTTPS product page URL using the standard port.');
        const response = await fetchPage('/api/check', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ url }), signal: AbortSignal.timeout(30000)
        });
        if (response.status === 429) throw new Error('Too many checks. Please try this URL again in a minute.');
        const data = await response.json();
        if (!response.ok) throw new Error(data.error || 'The check could not be completed.');
        if (!['available', 'unavailable', 'unknown'].includes(data.status)) throw new Error('The server returned an unexpected result.');
        result = data;
      } catch (error) {
        result = { status: 'unknown', message: error.name === 'TimeoutError' ? 'The check timed out. Please try this URL again.' : error.message || 'The check could not be completed.' };
      }
      onResult(index, result);
    }
  }
  await Promise.all([worker(), worker()]);
}
