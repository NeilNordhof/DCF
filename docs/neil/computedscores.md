# Computed Scores
This file contains definitions for calculating fantasy scores from raw recap scores, and what to do with these computed scores.

## Computing the scores
Any given score will start using the TotalScore value of a Score entity, we will assume that this score as pulled from dci.org is computed correctly from the repetoire and performance scores associated with it. This number will be from 0-20.

Depending on the leagues configurations, the various caption options will be computed differently.

### General Effect (GE)

- If there are double GE judges (two GE1 scores, two GE2 scores), then we average each one to have 1 GE1 score and 1 GE2 score.
- **Combined GE**: Our averaged GE1 and GE2 scores are added together, to give us a score out of 40 points.
- **Split GE**: We use our averaged scores as-is, for 2 separate 20 pointt scores.

### Visual

- **Combined Visual**: We take our 3 visual subcaptions (Visual Proficiency, Visual Analysis, Color Guard), and we add them together, then divide by 2, giving us a total out of 30 points.
- **Visual + Colorguard** aka Visual 2-Split: The Visual score is the average of Visual Proficiency and Visual Analysis scores, while Color Guard is just the Color Guard subcaption score, giving us 2 separate 20 point scores.
- **Visual Proficiency + Visual Analysis + Colorguard** aka Visual 3-Split: Each caption takes its respective score as-is, giving us 3 separate 20 point scores.

### Music

- If there are double Music Analysis Judges, then we average it to have 1 Music Analysis score.
- **Combined Music**: We take our 3 music subcaptions (Brass, Music Analysis, Percussion), and we add them together, then divide by 2, giving us a total out of 30 points.
- **Brass + Percussion** aka Music 2-Split: The Brass and Percussion scores are taken as-is, with Music Analysis being ignored, giving us 2 separate 20 point scores.
- **Brass + Music Analysis + Percussion** aka Music 3-split: Each caption takes its respective score as-is, giving us 3 separate 20 point scores.

## When to compute these
Since these scores are standardized across all leagues, we don't need to compute them each time we want to pull scores. We should instead compute these as we scrape the scores from dci.org, and store them in a new ComputedScores database table. This table will contain 1 row per corps per season, and as each corps gets a new score, their row will be updated with it. When a season starts, a row will be created for all participating corps with their scores set to 0.

A `ComputedScoreEntity` will contain the following values:
- `Id` (Guid)
- `SeasonId` (Guid FK)
- `CorpsId` (Guid FK)
- `GeneralEffectCombined` (double)
- `GeneralEffect1` (double)
- `GeneralEffect2` (double)
- `VisualCombined` (double)
- `Visual` (double)
- `Colorguard` (double)
- `Visual Proficiency` (double)
- `Visual Analysis` (double)
- `MusicCombined` (double)
- `Brass` (double)
- `Percussion` (double)
- `MusicAnalysis` (double)

We will also want a new `ComputedCaption` Enum to be used instead of the existing `Captions` Enum - Since our scores for the fantasy league are not 1-1 with DCI's scores, it would be better to have a separate enum for them instead of trying to leverage one enum for both score sets.

## Using the Computed Scores

When a standings api call is made, we need to compute the scores for a given league based on the users's picked corps. This will, in general, involve querying our new ComputedScores table for the computed scores for the corps that user drafted for a caption.
- For example, if a user is in a leauge with 2 corps per caption and Combined GE, and they drafted the Blue Devils and The Colts for Combined GE, then we would want to pull just the Combined GE scores for those two corps for the next calculation
Their final caption score would then be the average of the corps that they drafted for that caption. This final score, along with the individual corps's scores, ~~will need to be stored in a `ComputedScore` object that `MemberStanding` will now have.~~ With the work done for the front end stying, we can utilize the types added there instead. One minor change will be need, noted below
- Continuing the above example, we would then average Blue Devils' and Colts' Combined GE scores to get the user's current Combined GE score

~~Our `ComputedScore` object would look something like~~
- ~~ `CorpsScores` (`Dictionary<string, double>`)~~
- ~~`CaptionScore` (double)~~

And `MemberStanding` will have a `Dictionary<ComputedCaption, ComputedScore>` in addition to its current properties. This is a slight change from the current implementation, replacing string keys with our new enum for computed captions.

This process continues for each user and each caption, continuing to fill out the `MemberStanding`s dictionary of scores. Once they are all calculated, we will then need to compute the total score.

### Total Computed Score

This will not be a straight sum of the various computed caption scores for each member - we instead want to mirror the score weighting that DCI uses for their scores: 40% GE, 30% Visual, 30% Music. This means that we will need to adjust our calculations based on the caption setups the league uses. Thankfully, these are independent of each other (the selection for GE does not affect Visual for example).

- For GE, we can use the computed scores as-is, they will come out to a 40 point total.
- For Visual and Music, we want to end with a 30 point total:
    - For Combined, we can use the 30 point score as-is.
    - For 2-Split, we want 75% of the computed score, turning each score into a 15 point value.
    - For 3-Split, we want 50% of the computed score, turning each score into a 10 point value.

With any weighting applied as described above, the various computed caption scores can then be summed into a 100 point total, giving the player their current score. This will be stored in the `Score` property of a `MemberStanding`

## Addressing concerns
Why store all this extra info? It feel very extra. Its to cut down on redundant standings calculations every time a different league is loaded up. We can just pull the scores we need for fantasy directly, and not worry about anything DCI has that we dont use.

Do we still need to store the raw DCI scores then? Yes, I would like to for now. Eventually, I would like to have page for viewing DCI's scores separate from Fantasy. It would also be cool to include some graphs showing season progress or comparing various corps and captions (you will soon learn I **REALLY** like graphs and charts).

Speaking of graphs and charts, we will probably eventually have some for the Fantasy side, showing season progress and score breakdowns, but that is a future effort.