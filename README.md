# Task5 — Movie Catalog Generator

An educational ASP.NET Core application with a web interface. It supports English and Russian, pagination, table and gallery views, and configurable seed and average like/review counts.

## How it works

1. `GET /api/movies` derives each film's `movieSeed` from the user seed, locale, and sequence index.
2. Using `movieSeed`, Bogus generates the year, actors, and director; the title is assembled from local JSON dictionaries.
3. When a film is opened, `POST /api/movies/movie` builds a description and 10 reviews from local JSON resources while FFmpeg creates a 10-second trailer. Video segments are selected using `movieSeed`, music is chosen by genre, and animated credits are overlaid.
4. Like and visible review counts depend on `movieSeed` and the selected averages. The interface displays the appropriate number of the 10 generated reviews.
5. Text and trailer bytes are cached in memory for up to four hours. `GET /api/movies/{movieSeed}/trailer` serves the video. The cache is rebuilt after an application restart.

The application uses neither a persistent database nor an external AI service.
