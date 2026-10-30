-- CipherVault V001: Initial Schema
-- This file contains the initial database schema for CipherVault.
-- It is executed by SchemaMigrator.InitializeSchema() and serves as a reference document.

-- 保险库元信息
CREATE TABLE IF NOT EXISTS vault_meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- 密码条目
CREATE TABLE IF NOT EXISTS entries (
    id              TEXT PRIMARY KEY,
    title           TEXT NOT NULL,
    credential_type TEXT NOT NULL DEFAULT 'login',
    username        TEXT,
    password_enc    BLOB,
    url             TEXT,
    notes           TEXT,
    totp_secret     TEXT,
    is_favorite     INTEGER NOT NULL DEFAULT 0,
    group_id        TEXT,
    custom_fields   TEXT,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    modified_at     TEXT NOT NULL DEFAULT (datetime('now')),
    last_accessed_at TEXT,
    deleted_at      TEXT
);

-- 分组
CREATE TABLE IF NOT EXISTS groups (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    parent_id   TEXT,
    sort_order  INTEGER NOT NULL DEFAULT 0,
    icon_name   TEXT,
    FOREIGN KEY (parent_id) REFERENCES groups(id) ON DELETE SET NULL
);

-- 标签
CREATE TABLE IF NOT EXISTS tags (
    id   TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    color TEXT
);

-- 条目-标签关联
CREATE TABLE IF NOT EXISTS entry_tags (
    entry_id TEXT NOT NULL,
    tag_id   TEXT NOT NULL,
    PRIMARY KEY (entry_id, tag_id),
    FOREIGN KEY (entry_id) REFERENCES entries(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id)   REFERENCES tags(id)   ON DELETE CASCADE
);

-- 条目历史
CREATE TABLE IF NOT EXISTS entry_history (
    id                 TEXT PRIMARY KEY,
    entry_id           TEXT NOT NULL,
    timestamp          TEXT NOT NULL DEFAULT (datetime('now')),
    snapshot_json      TEXT NOT NULL,
    change_description TEXT,
    FOREIGN KEY (entry_id) REFERENCES entries(id) ON DELETE CASCADE
);

-- 索引
CREATE INDEX IF NOT EXISTS idx_entries_title    ON entries(title);
CREATE INDEX IF NOT EXISTS idx_entries_group    ON entries(group_id);
CREATE INDEX IF NOT EXISTS idx_entries_favorite ON entries(is_favorite) WHERE is_favorite = 1;
CREATE INDEX IF NOT EXISTS idx_entries_modified ON entries(modified_at DESC);
CREATE INDEX IF NOT EXISTS idx_entries_accessed ON entries(last_accessed_at DESC);
CREATE INDEX IF NOT EXISTS idx_entries_deleted  ON entries(deleted_at) WHERE deleted_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_entry_history    ON entry_history(entry_id, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_groups_parent    ON groups(parent_id);
