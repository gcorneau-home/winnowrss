-- Trusted feeds: their articles are never filtered. (Keywords are filter_criteria rows with kind 2.)
ALTER TABLE feeds ADD COLUMN skip_filter INTEGER NOT NULL DEFAULT 0;
