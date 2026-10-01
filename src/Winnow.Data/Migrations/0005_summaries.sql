-- Summaries written by the model on request, one per article and language (a new one replaces the old).
CREATE TABLE article_summaries (
    article_id INTEGER NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    language   TEXT    NOT NULL, -- two-letter code: en, fr, es...
    text       TEXT    NOT NULL,
    model      TEXT    NOT NULL,
    created_at TEXT    NOT NULL,
    PRIMARY KEY (article_id, language)
);
