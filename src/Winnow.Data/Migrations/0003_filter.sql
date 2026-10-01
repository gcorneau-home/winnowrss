-- Interest filter: verdicts are stored per article, rejected articles stay visible (grayed out) instead of
-- going to the trash, and filtering happens after the refresh (Pending until a model has judged the article).

ALTER TABLE articles ADD COLUMN filter_status    INTEGER NOT NULL DEFAULT 0; -- 0 unfiltered, 1 pending, 2 kept, 3 rejected, 4 error
ALTER TABLE articles ADD COLUMN filter_criterion TEXT;
ALTER TABLE articles ADD COLUMN filter_version   INTEGER;
ALTER TABLE articles ADD COLUMN filter_model     TEXT;
ALTER TABLE articles ADD COLUMN filtered_at      TEXT;

-- Until now only the pass-through filter existed: its "kept" verdicts were never a real judgement.
UPDATE articles SET filter_status = CASE WHEN filter_kept = 0 THEN 3 ELSE 0 END;
ALTER TABLE articles DROP COLUMN filter_kept;

CREATE INDEX ix_articles_filter_status ON articles(filter_status);

CREATE TABLE filter_criteria (
    id         INTEGER PRIMARY KEY,
    kind       INTEGER NOT NULL,           -- 0 interest, 1 exclusion
    text       TEXT    NOT NULL,
    enabled    INTEGER NOT NULL DEFAULT 1,
    sort_order INTEGER NOT NULL DEFAULT 0
);

-- Starting criteria from the project brief; the user edits them in the Filter window.
INSERT INTO filter_criteria (kind, text, sort_order) VALUES
    (0, 'Programmation Python', 1),
    (0, 'Programmation C# et .NET', 2),
    (0, 'Environnements et outils de développement (IDE, éditeurs, terminaux, Git)', 3),
    (0, 'Développement assisté par IA (assistants de code, agents, LLM pour développeurs)', 4),
    (0, 'Utilitaires Windows', 5),
    (0, 'Nouvelles applications et logiciels', 6),
    (1, 'Bons plans, promotions et liens affiliés', 7),
    (1, 'Apple et Mac (iPhone, iPad, macOS, matériel Apple)', 8),
    (1, 'Consoles de jeu (PlayStation, Xbox, Nintendo, jeux sur console)', 9);
