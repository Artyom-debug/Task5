'use strict';

const translations = {
  'en-US': {
    randomSeedShort: 'Random',
    movies: 'Movies', subtitle: 'Catalog generator', locale: 'Language / region',
    seed: 'Seed', randomSeed: 'Generate a random seed', likes: 'Likes', reviews: 'Reviews', view: 'View',
    english: 'English (USA)', russian: 'Russian (Russia)', table: 'Table', gallery: 'Gallery',
    number: '#', genre: 'Genre', title: 'Title', actors: 'Cast', year: 'Year',
    previous: '← Previous', next: 'Next →', page: 'Page', parameters: 'Generation settings',
    avgLikes: 'Average likes per movie', avgReviews: 'Average reviews per movie',
    generatedMovies: 'Generated movies', movieGallery: 'Movie gallery', pages: 'Catalog pages',
    invalidSeed: 'Seed: an integer between 0 and 18446744073709551615.',
    invalidReviews: 'Reviews: a number from 0 to 10 in increments of 0.1.',
    loadingDetails: 'Loading description and trailer…',
    detailsError: 'Could not load details. Collapse and reopen the movie to try again.',
    trailer: 'Trailer', trailerError: 'Trailer unavailable. Reopen the movie to try again.',
    director: 'Director', noReviews: 'No reviews yet.', toggleDetails: 'Show or hide details',
    loadingMovies: 'Loading movies…', noMovies: 'No more movies.',
    moviesError: 'Could not load movies. Check your connection and change a setting to try again.',
    detailsLink: 'Details and trailer ↗', updating: 'Updating…'
  },
  'ru-RU': {
    randomSeedShort: 'Случайный',
    movies: 'Фильмы', subtitle: 'Генератор каталога', locale: 'Язык / регион',
    seed: 'Сид', randomSeed: 'Сгенерировать случайный сид', likes: 'Лайки', reviews: 'Ревью', view: 'Вид',
    english: 'Английский (США)', russian: 'Русский (Россия)', table: 'Таблица', gallery: 'Галерея',
    number: '№', genre: 'Жанр', title: 'Название', actors: 'Актёры', year: 'Год',
    previous: '← Назад', next: 'Далее →', page: 'Страница', parameters: 'Параметры генерации',
    avgLikes: 'Среднее число лайков', avgReviews: 'Среднее число ревью',
    generatedMovies: 'Сгенерированные фильмы', movieGallery: 'Галерея фильмов', pages: 'Страницы каталога',
    invalidSeed: 'Сид: целое число от 0 до 18446744073709551615.',
    invalidReviews: 'Ревью: число от 0 до 10 с шагом 0,1.',
    loadingDetails: 'Загрузка описания и трейлера…',
    detailsError: 'Не удалось загрузить детали. Закройте и снова раскройте фильм, чтобы повторить.',
    trailer: 'Трейлер', trailerError: 'Трейлер недоступен. Раскройте фильм повторно.',
    director: 'Режиссёр', noReviews: 'Пока нет ревью.', toggleDetails: 'Показать или скрыть детали',
    loadingMovies: 'Загрузка фильмов…', noMovies: 'Больше фильмов нет.',
    moviesError: 'Не удалось загрузить фильмы. Проверьте соединение и измените параметр для повтора.',
    detailsLink: 'Детали и трейлер ↗', updating: 'Обновление…'
  }
};
