-- Shared helpers for the SaveHarbor pgTAP tests. Every test file runs in its own transaction and rolls back,
-- so nothing here survives a test run. Files run in name order; this one only checks that pgTAP is present.
begin;
create extension if not exists pgtap with schema extensions;
select plan(2);
select has_function('public', 'publish_world_version', 'publish_world_version exists');
select has_function('public', 'acquire_session_lock', 'acquire_session_lock exists');
select * from finish();
rollback;
