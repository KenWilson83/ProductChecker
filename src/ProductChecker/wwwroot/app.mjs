import { parseUrls, isProductUrl, checkUrls } from './checks.mjs';

const form = document.querySelector('#check-form');
const results = document.querySelector('#results');
const progress = document.querySelector('#progress');
const errorMessage = document.querySelector('#form-error');
const button = document.querySelector('#submit');
const input = document.querySelector('#urls');

form.addEventListener('submit', async event => {
  event.preventDefault();
  if (button.disabled) return;
  errorMessage.textContent = '';
  input.removeAttribute('aria-invalid');
  let urls;
  try { urls = parseUrls(input.value); }
  catch (error) {
    errorMessage.textContent = error.message;
    input.setAttribute('aria-invalid', 'true');
    input.focus();
    return;
  }
  button.disabled = true;
  button.textContent = 'Checking…';
  let completed = 0;
  progress.textContent = `Checked 0 of ${urls.length} products.`;
  const cards = urls.map((url, index) => {
    const card = document.createElement('article'); card.className = 'result';
    const heading = document.createElement('h2'); heading.textContent = `Product ${index + 1}: Checking…`;
    const address = document.createElement('p'); address.className = 'product-url'; address.textContent = url;
    const message = document.createElement('p'); message.textContent = 'Waiting for availability.';
    card.append(heading, address, message);
    if (isProductUrl(url)) {
      const link = document.createElement('a'); link.textContent = 'View product at the store';
      link.href = url; link.target = '_blank'; link.rel = 'noopener noreferrer'; card.append(link);
    }
    return { card, heading, message };
  });
  results.replaceChildren(...cards.map(item => item.card));
  try {
    await checkUrls(urls, (index, data) => {
      const { card, heading, message } = cards[index];
      card.dataset.status = data.status;
      heading.textContent = `Product ${index + 1}: ${{ available: 'Available', unavailable: 'Unavailable', unknown: 'Couldn’t determine availability' }[data.status]}`;
      message.textContent = data.message;
      progress.textContent = `Checked ${++completed} of ${urls.length} products.`;
    });
  } finally {
    button.disabled = false;
    button.textContent = 'Check availability';
  }
});
