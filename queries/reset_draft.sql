-- Reset a league's draft back to not-started. Set league_id below, then run
-- the whole file — works via `psql -f queries/reset_draft.sql` or pasted
-- directly into pgAdmin's Query Tool.
DO $$
DECLARE
	league_id uuid := 'c9ad1d9f-c05f-4b1f-b2d9-31b196b7b3ab';
BEGIN
	UPDATE public."Leagues"
		SET "DraftStatus"=0, "DraftStartTime"=null, "DraftOrderJson"='[]', "CurrentPickNumber"=0
		WHERE "Id"=league_id;

	DELETE FROM public."DraftPicks"
		WHERE "LeagueId"=league_id;
END $$;
