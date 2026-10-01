'use strict';

const seed = document.querySelector('#seed');
const likes = document.querySelector('#likes');
const reviews = document.querySelector('#reviews');
const rows = document.querySelector('#movies');
const status = document.querySelector('#status');
const sentinel = document.querySelector('#sentinel');
const pageSize = 20;
const locale = document.querySelector('#locale');
const view = document.querySelector('#view');
const gallery = document.querySelector('#gallery');
const pagination = document.querySelector('#pagination');
const previous = document.querySelector('#previous');
const next = document.querySelector('#next');
let page = 1;
let generation = 0;
let loading = false;
let failed = false;
let timer;
let listController;
let selected;

function t(key) {
  return translations[locale.value][key];
}

function localize() {
  document.documentElement.lang = locale.value;
  document.title = t('movies');
  document.querySelectorAll('[data-i18n]').forEach(node => {
    node.textContent = t(node.dataset.i18n);
  });
  document.querySelectorAll('[data-i18n-aria]').forEach(node => {
    node.setAttribute('aria-label', t(node.dataset.i18nAria));
  });
  document.querySelector('#page-number').textContent = `${t('page')} ${page}`;
  document.querySelector('#likes-value').value = Number(likes.value).toLocaleString(locale.value);
}

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function message(text, error = false) {
  status.textContent = text;
  status.classList.toggle('error', error);
}

function validParameters() {
  const validSeed = /^\d{1,20}$/.test(seed.value) && BigInt(seed.value) <= 18446744073709551615n;
  const validReviews = reviews.value !== '' && reviews.validity.valid;
  seed.setAttribute('aria-invalid', String(!validSeed));
  reviews.setAttribute('aria-invalid', String(!validReviews));
  if (!validSeed) message(t('invalidSeed'), true);
  else if (!validReviews) message(t('invalidReviews'), true);
  return validSeed && validReviews;
}

async function jsonRequest(url, options = {}) {
  const response = await fetch(url, options);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}

function collapse() {
  if (!selected) return;
  selected.controller.abort();
  selected.detail.querySelector('video')?.pause();
  selected.row.setAttribute('aria-expanded', 'false');
  selected.row.removeAttribute('aria-controls');
  selected.detail.remove();
  selected = null;
}

async function expand(row, movie) {
  if (selected?.row === row) { collapse(); return; }
  collapse();
  const isCard = view.value === 'gallery';
  const detail = element(isCard ? 'div' : 'tr');
  detail.id = `detail-${movie.sequenceIndex}`;
  const cell = element(isCard ? 'div' : 'td', 'detail-cell', t('loadingDetails'));
  cell.colSpan = 5;
  cell.setAttribute('aria-live', 'polite');
  detail.append(cell);
  row.after(detail);
  row.setAttribute('aria-expanded', 'true');
  row.setAttribute('aria-controls', detail.id);
  const controller = new AbortController();
  selected = { row, detail, controller };
  try {
    const data = await jsonRequest('/api/movies/movie', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: controller.signal,
      // The seed stays a string in list requests, preserving all 64 bits.
      body: JSON.stringify({ movie, locale: locale.value, avgLikes: Number(likes.value), avgReviews: Number(reviews.value) })
    });
    if (controller.signal.aborted) return;
    renderDetails(cell, movie, data);
  } catch (error) {
    if (error.name !== 'AbortError') {
      cell.textContent = t('detailsError');
      cell.classList.add('error');
    }
  }
}

function renderDetails(cell, movie, data) {
  const content = data.expandedMovie;
  const layout = element('div', 'details');
  const media = element('div', 'media');
  const video = element('video');
  video.controls = true;
  video.preload = 'metadata';
  video.playsInline = true;
  video.setAttribute('aria-label', `${t('trailer')}: ${movie.title}`);
  // Use the known same-origin endpoint, never an arbitrary URL from generated data.
  video.src = `/api/movies/${movie.movieSeed}/trailer`;
  video.addEventListener('error', () => {
    video.replaceWith(element('p', 'error', t('trailerError')));
  }, { once: true });
  media.append(video, element('span', 'likes-badge', `♥ ${content.likesCount}`));
  const copy = element('div');
  const cast = element('p', 'credits');
  cast.append(element('strong', '', `${t('actors')}: `), document.createTextNode(movie.actors.join(', ')));
  const director = element('p', 'credits');
  director.append(element('strong', '', `${t('director')}: `), document.createTextNode(movie.director));
  copy.append(element('h2', '', movie.title), element('span', 'meta', `${movie.year}, ${movie.genre}`),
    cast, director,
    element('p', 'description', content.description),
    element('h3', 'review-heading', `${t('reviews')} · ${content.reviewsCount}`));
  for (const review of content.reviews.slice(0, content.reviewsCount)) copy.append(element('p', 'review', review));
  if (!content.reviewsCount) copy.append(element('p', 'meta', t('noReviews')));
  layout.append(media, copy);
  cell.replaceChildren(layout);
}

function movieRow(movie) {
  const row = element('tr', 'movie-row');
  row.tabIndex = 0;
  row.setAttribute('aria-expanded', 'false');
  row.setAttribute('aria-label', `${movie.sequenceIndex}. ${movie.title}. ${t('toggleDetails')}`);
  const number = element('td', 'number');
  const chevron = element('span', 'chevron');
  chevron.setAttribute('aria-hidden', 'true');
  number.append(chevron, document.createTextNode(movie.sequenceIndex));
  row.append(number, element('td', '', movie.genre), element('td', '', movie.title),
    element('td', '', movie.actors.join(', ')), element('td', 'year', movie.year));
  row.addEventListener('click', () => expand(row, movie));
  row.addEventListener('keydown', event => {
    if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); expand(row, movie); }
  });
  return row;
}

async function loadPage() {
  if (loading || failed || !validParameters()) return;
  const currentGeneration = generation;
  loading = true;
  updateNavigation();
  listController = new AbortController();
  message(t('loadingMovies'));
  try {
    const query = new URLSearchParams({ PageNumber: page, PageSize: pageSize, seed: seed.value, locale: locale.value });
    const movies = await jsonRequest(`/api/movies?${query}`, { signal: listController.signal });
    if (currentGeneration !== generation) return;
    const fragment = document.createDocumentFragment();
    movies.forEach(movie => fragment.append(view.value === 'table' ? movieRow(movie) : movieCard(movie)));
    if (view.value === 'table') rows.replaceChildren(fragment);
    else gallery.append(fragment);
    document.querySelector('#page-number').textContent = `${t('page')} ${page}`;
    if (view.value === 'gallery') page++;
    failed = movies.length === 0;
    message(failed ? t('noMovies') : '');
  } catch (error) {
    if (error.name !== 'AbortError' && currentGeneration === generation) {
      failed = true;
      message(t('moviesError'), true);
    }
  } finally {
    if (currentGeneration === generation) {
      loading = false;
      updateNavigation();
      if (view.value === 'gallery' && !failed && sentinel.getBoundingClientRect().top < window.innerHeight + 200) loadPage();
    }
  }
}

function movieCard(movie) {
  const card = element('article', 'movie-card');
  const toggle = element('button', 'card-summary');
  toggle.type = 'button';
  toggle.setAttribute('aria-expanded', 'false');
  toggle.setAttribute('aria-label', `${movie.title}. ${t('toggleDetails')}`);
  toggle.append(element('span', 'card-meta', `${t('number')} ${movie.sequenceIndex} · ${movie.genre} · ${movie.year}`),
    element('strong', 'card-title', movie.title), element('span', 'credits', movie.actors.join(', ')),
    element('span', 'card-hint', t('detailsLink')));
  toggle.addEventListener('click', () => expand(toggle, movie));
  card.append(toggle);
  return card;
}

function updateNavigation() {
  previous.disabled = loading || page <= 1;
  next.disabled = loading || failed;
}

function reset(pageNumber = 1) {
  clearTimeout(timer);
  generation++;
  listController?.abort();
  collapse();
  rows.replaceChildren();
  gallery.replaceChildren();
  page = pageNumber;
  localize();
  loading = false;
  failed = false;
  const tableMode = view.value === 'table';
  document.querySelector('.table-wrap').hidden = !tableMode;
  gallery.hidden = tableMode;
  pagination.hidden = !tableMode;
  sentinel.hidden = tableMode;
  window.scrollTo({ top: 0, behavior: 'instant' });
  loadPage();
}

function parametersChanged() {
  localize();
  clearTimeout(timer);
  generation++;
  listController?.abort();
  collapse();
  loading = false;
  failed = true;
  rows.replaceChildren();
  gallery.replaceChildren();
  updateNavigation();
  if (!validParameters()) return;
  message(t('updating'));
  timer = setTimeout(() => {
    reset();
  }, 300);
}

for (const input of [seed, likes, reviews, locale]) input.addEventListener('input', parametersChanged);
view.addEventListener('change', () => reset());
previous.addEventListener('click', () => reset(page - 1));
next.addEventListener('click', () => reset(page + 1));
new IntersectionObserver(entries => {
  if (view.value === 'gallery' && entries.some(entry => entry.isIntersecting)) loadPage();
}, { rootMargin: '200px' }).observe(sentinel);
reset();
