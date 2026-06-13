-- Smoke test teardown. Safe to run at any time — no-op if smoke data absent.
-- Order respects FK constraints.

DELETE FROM "Scores"
    WHERE "ShowId" IN (SELECT "Id" FROM "Shows" WHERE "Name" = 'Smoke Show');

DELETE FROM "ComputedScores"
    WHERE "ShowId" IN (SELECT "Id" FROM "Shows" WHERE "Name" = 'Smoke Show');

DELETE FROM "DraftPicks"
    WHERE "LeagueId" IN (SELECT "Id" FROM "Leagues" WHERE "Name" = 'Smoke League');

DELETE FROM "LeagueMembers"
    WHERE "LeagueId" IN (SELECT "Id" FROM "Leagues" WHERE "Name" = 'Smoke League');

DELETE FROM "Leagues" WHERE "Name" = 'Smoke League';

DELETE FROM "ShowCorps"
    WHERE "ShowId" IN (SELECT "Id" FROM "Shows" WHERE "Name" = 'Smoke Show');

DELETE FROM "Shows" WHERE "Name" = 'Smoke Show';

DELETE FROM "SeasonCorps"
    WHERE "SeasonId" IN (SELECT "Id" FROM "Seasons" WHERE "Year" = 9999);

DELETE FROM "Seasons" WHERE "Year" = 9999;

DELETE FROM "Corps" WHERE "Name" LIKE 'Smoke Corps %';

DELETE FROM "Users" WHERE "Auth0Sub" LIKE 'smoke-%';
