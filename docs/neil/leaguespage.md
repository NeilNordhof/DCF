# Leagues Page Updates

## Tabbed Layout

Adopt a tabbed layout for the Leagues page:
- My Leagues : A list of leagues that the user is in.
- Join: This is where the user can browse leagues, or join by id

### My Leagues

This will be the home to any leagues the user is in. The featured league will be the users favorited league - a new value we will need to store. The first league they join will be their favorite. They can change their favorite league by going into a league's details and pressing a start button to set their new favorite league.

If the user is not in any leagues, they will instead be shown a message saying 
`You are not currently in any leagues, Join a league or Create your own!`
Join a league will be a link or button that navigates to the Join tab, and Create your own will be the same but to the Create page

### Join

This will be the home to the leagues browsing. The at the top will be a message saying

`Browse and join a public league, or join by code: ` With a text field and button to lookup a league by code.

If there are no public leagues available, then a similar message from the My Leagues tab would appear:

`There are no public leagues to join, Create one now!` With a link/button to the Create tab

The user will not be able to join a league directly from the browse page. Clicking on a league or looking on up by code will bring them to the League Details page, with some limits on the tabs they can view.

## Create page updates

I'd like to add at some things to the create page, the first being a max number of players for a league.

The Max number of players can be configurable, probably a minimum of 4 players, and then the max will be determined by the number of corps in the season divided by the number of corps a player can draft per caption, rounded down. So if a season has 16 corps, and the league is set to 3 corps per caption, then the limit for max players in a league is 5. This also means two things:
- A season must have a minimum of 4 corps to be valid
- The number of corps per caption is capped by the number of corps in a season divided by 4 (minimum players in a league)

Lets also add a cancel button by the submit button, with a warning dialog about any changes being lost upon being clicked. Clicking ok/confirm on the dialog will return them to the leagues page. This same dialog should be shown if the user tries to navigate away from the page.

## Leagues details changes

If someone navigates to a league and is not in it, they will have some limitations:
- Can not click the Join Draft button if it is available

They will instead be presented with a couple of buttons:
- `<- Browse`, which returns them to the leagues page on the browse tab, and this button is always available.
- `Join Leauge`, and this button is only available if:
  - The User is not in the league
  - The League is not at max members
  - The League has not started its draft

Users can still view the rest of the league`s details if they are not a member of the league.

With member limitation now in place, a commissioner can now only start a draft if a league has at least 4 members in it. If the draft is scheduled, and the league does not have at least 4 members, then it will fail to start, and will become unscheduled.