# Admin Page Updates

- If the user is an admin, allow them to see an admin button in the nav bar next to profile.

## Corps Tab
- Add the ability to edit a corps name, even if they belong to a published season.
- Add the ability to delete corps only if they are not part of a published season. Once a season is published, the corps is not longer deletable
- I would like to add an icon field for corps - lets discuss this though.

## Seasons Tab
- Move Add Season panel to top, make collapsable
- Allow Start and End Dates to be editable, until the season is published
    - This is done on the season detail page
- Add a warning when Publish is pressed, with the option for confirm publish or cancel publish.

## Shows List
- Move Add Show panel to top of page, make collapsable
- Add a `Time Zone` dropdown
    - If possible, can this be set into various date properties once selected? Aka not stored separately
    - Only possible Time zones are PT, MT, CT, ET.
- Add a `Start Time` field
    - This is just the time, date will be inferred from the Show Date
- Change Scores Announced time to be just the time
    - Same as Start Time, date will be inferred from Show data
- Move labels for fields to left of field, and add labels for start time and scores time.
- Order fields so that Date and Time zone are on one row, and then Start and Scores times are the next row
- If not already, shows are sorted by date (earliest to latest)
- To edit a show, expand a show card (down arrow on right of card that expands it) and have the same editable fields as Add Show
    - Only one expanded at a time - opening another should close the rest.
    - These are editable until a show's start time passes.
    - The Add Show button will be replaced with a `Delete Show` button and `Save` button
    - Once a show has started, these buttons are replaced with a `Trigger Score Scrape`
- If not already enforced, dates and times are limited by the season start and end dates
- If not already enforced, dates and times can not be in the past
- Shows can still be added, edited, or deleted if a season has been published and even started.

## Other
- Create League is not available if there is not an active season. This can be a message in place of the Create League fields.