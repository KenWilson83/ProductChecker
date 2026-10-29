import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseUrls, checkUrls } from '../../src/ProductChecker/wwwroot/checks.mjs';

test('multiline input ignores blanks and duplicates, preserving order', () => {
  assert.deepEqual(parseUrls(' https://a.example/p \r\n\nhttps://b.example/p\nhttps://a.example/p'), ['https://a.example/p', 'https://b.example/p']);
  assert.throws(() => parseUrls(' \n '), /at least one/);
  assert.throws(() => parseUrls(Array.from({ length: 11 }, (_, i) => `https://store.example/${i}`).join('\n')), /no more than 10/);
});

test('mixed outcomes remain associated with their input URLs and failures do not stop the batch', async () => {
  const urls = ['https://store.example/available', 'bad URL', 'https://store.example/fails', 'https://store.example/unavailable', 'https://store.example/unknown'];
  const results = [], requests = [];
  await checkUrls(urls, (index, result) => { results[index] = result; }, async (_, options) => {
    const { url } = JSON.parse(options.body); requests.push(url);
    if (url.endsWith('/fails')) throw new Error('Network failure');
    return { ok: true, status: 200, json: async () => ({ status: url.split('/').at(-1), message: url }) };
  });
  assert.equal(requests.length, 4);
  assert.deepEqual(results.map(r => r.status), ['available', 'unknown', 'unknown', 'unavailable', 'unknown']);
  assert.match(results[1].message, /valid HTTPS/);
  assert.equal(results[2].message, 'Network failure');
  assert.equal(results[3].message, urls[3]);
});

test('rate limits and malformed responses are reported per URL', async () => {
  const results = [];
  await checkUrls(['https://store.example/limit', 'https://store.example/broken'], (index, result) => { results[index] = result; }, async (_, options) => {
    if (JSON.parse(options.body).url.endsWith('/limit')) return { status: 429 };
    return { ok: true, status: 200, json: async () => { throw new SyntaxError('Invalid JSON'); } };
  });
  assert.equal(results[0].status, 'unknown'); assert.match(results[0].message, /minute/);
  assert.equal(results[1].status, 'unknown');
});

test('at most two requests run concurrently, with stable result indexes', async () => {
  let active = 0, peak = 0;
  const results = [];
  const urls = Array.from({ length: 10 }, (_, i) => `https://store.example/${i}`);
  await checkUrls(urls, (index, result) => { results[index] = result.message; }, async (_, options) => {
    peak = Math.max(peak, ++active);
    const { url } = JSON.parse(options.body);
    await new Promise(resolve => setTimeout(resolve, url.endsWith('/0') ? 20 : 1));
    active--;
    return { ok: true, status: 200, json: async () => ({ status: 'available', message: url }) };
  });
  assert.equal(peak, 2); assert.deepEqual(results, urls);
});
