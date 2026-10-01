-- Dates are stored as ISO 8601 UTC text ("O" format), so they sort correctly as strings.

CREATE TABLE categories (
    id         INTEGER PRIMARY KEY,
    name       TEXT    NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE feeds (
    id              INTEGER PRIMARY KEY,
    category_id     INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    title           TEXT    NOT NULL,
    url             TEXT    NOT NULL UNIQUE,
    site_url        TEXT,
    e_tag           TEXT,
    last_modified   TEXT,
    last_fetched_at TEXT,
    last_error      TEXT
);

CREATE INDEX ix_feeds_category ON feeds(category_id);

CREATE TABLE articles (
    id            INTEGER PRIMARY KEY,
    feed_id       INTEGER NOT NULL REFERENCES feeds(id) ON DELETE CASCADE,
    dedup_key     TEXT    NOT NULL,
    link          TEXT,
    title         TEXT    NOT NULL,
    author        TEXT,
    published_at  TEXT,
    summary       TEXT,
    content_html  TEXT,
    content_text  TEXT,
    is_read       INTEGER NOT NULL DEFAULT 0,
    is_pinned     INTEGER NOT NULL DEFAULT 0,
    rating        INTEGER NOT NULL DEFAULT 0,  -- -1 down, 0 none, 1 up
    state         INTEGER NOT NULL DEFAULT 0,  -- 0 active, 1 archived, 2 trashed, 3 purged
    trashed_at    TEXT,
    archived_at   TEXT,
    filter_kept   INTEGER,
    filter_reason TEXT,
    fetched_at    TEXT    NOT NULL,
    UNIQUE (feed_id, dedup_key)
);

CREATE INDEX ix_articles_feed_state ON articles(feed_id, state);
CREATE INDEX ix_articles_state ON articles(state);

CREATE TABLE article_tags (
    article_id INTEGER NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    name       TEXT    NOT NULL,
    PRIMARY KEY (article_id, name)
);

-- Images of archived articles, so they stay readable offline.
CREATE TABLE archived_resources (
    id           INTEGER PRIMARY KEY,
    article_id   INTEGER NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    original_url TEXT    NOT NULL,
    content_type TEXT    NOT NULL,
    data         BLOB    NOT NULL,
    UNIQUE (article_id, original_url)
);

-- Full-text index over articles (external content table kept in sync by triggers).
CREATE VIRTUAL TABLE articles_fts USING fts5(
    title, author, summary, content_text,
    content = 'articles',
    content_rowid = 'id',
    tokenize = 'unicode61 remove_diacritics 2'
);

CREATE TRIGGER articles_fts_insert AFTER INSERT ON articles BEGIN
    INSERT INTO articles_fts (rowid, title, author, summary, content_text)
    VALUES (new.id, new.title, new.author, new.summary, new.content_text);
END;

CREATE TRIGGER articles_fts_delete AFTER DELETE ON articles BEGIN
    INSERT INTO articles_fts (articles_fts, rowid, title, author, summary, content_text)
    VALUES ('delete', old.id, old.title, old.author, old.summary, old.content_text);
END;

CREATE TRIGGER articles_fts_update AFTER UPDATE OF title, author, summary, content_text ON articles BEGIN
    INSERT INTO articles_fts (articles_fts, rowid, title, author, summary, content_text)
    VALUES ('delete', old.id, old.title, old.author, old.summary, old.content_text);
    INSERT INTO articles_fts (rowid, title, author, summary, content_text)
    VALUES (new.id, new.title, new.author, new.summary, new.content_text);
END;
