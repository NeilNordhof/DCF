export type DraftStatus = 'NotStarted' | 'Scheduled' | 'Open' | 'InProgress' | 'Completed';

export type ComputedCaption =
  | 'GeneralEffectCombined'
  | 'GeneralEffect1'
  | 'GeneralEffect2'
  | 'VisualCombined'
  | 'Visual'
  | 'Colorguard'
  | 'VisualProficiency'
  | 'VisualAnalysis'
  | 'MusicCombined'
  | 'Brass'
  | 'Percussion'
  | 'MusicAnalysis';

export interface League {
  id: string;
  name: string;
  isPublic: boolean;
  inviteCode?: string;
  commissionerUserId?: string;
  draftStatus: DraftStatus;
  draftStartTime?: string;
  corpsPerCaption?: number;
  draftableCaptions?: ComputedCaption[];
  seasonYear?: number;
  seasonId?: string;
  maxPlayers: number;
  memberCount: number;
  isMember?: boolean;
  isCommissioner?: boolean;
  userRank?: number;
  userScore?: number;
  members?: Member[];
  picks?: DraftPick[];
  issueMessages?: string[];
}

export interface PublicLeague {
  id: string;
  name: string;
  draftStatus: DraftStatus;
  memberCount: number;
  maxPlayers: number;
}

export interface ActiveSeason {
  id: string;
  year: number;
  corpsCount: number;
}

export interface Member {
  userId: string;
  displayName: string;
}

export interface DraftPick {
  userId: string;
  displayName: string;
  corpsId: string;
  corpsName: string;
  caption: ComputedCaption;
  pickNumber: number;
  roundNumber: number;
}

export interface Standing {
  userId: string;
  displayName: string;
  score: number;
  captions: Partial<Record<ComputedCaption, CaptionBreakdown>>;
}

export interface Corps {
  id: string;
  name: string;
  iconUrl?: string;
}

export interface SeasonCorps {
  id: string;
  name: string;
  iconUrl?: string;
  sortOrder?: number;
}

export interface DraftState {
  status: DraftStatus;
  draftStartTime?: string;
  currentPickNumber: number;
  currentDrafterId?: string;
  onlineUserIds?: string[];
  draftOrder: { userId: string; displayName: string }[];
  members: Member[];
  picks: DraftPick[];
  makeupQueue: string[];
  mainTotalPicks: number;
}

export interface PickPreview {
  userId: string;
  corpsId: string;
  caption: string;
}

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
  emailNotificationsEnabled: boolean;
}

export interface CreateLeagueRequest {
  name: string;
  isPublic: boolean;
  corpsPerCaption: number;
  maxPlayers: number;
  draftableCaptions: ComputedCaption[];
  draftStartTime?: string | null;
  draftTimezone?: string | null;
}

export interface UpdateLeagueRequest {
  corpsPerCaption: number;
  maxPlayers: number;
  draftableCaptions: ComputedCaption[];
  draftStartTime: string | null;
  draftTimezone: string | null;
}

export type SeasonStatus = 'Upcoming' | 'Active' | 'Completed';

export interface Season {
  id: string;
  year: number;
  startDate: string;
  endDate: string;
  status: SeasonStatus;
  isPublished: boolean;
}

export interface SeasonDetail extends Season {
  corpsIds: string[];
  corpsSortOrders: Record<string, number>;
}

export interface ShowScheduleEntry {
  time: string | null;
  label: string;
  corpsId: string | null;
}

export interface ShowPrefillScheduleEntry {
  time: string | null;
  label: string;
  corpsId: string | null;
}

export interface ShowPrefillResponse {
  location?: string;
  latitude?: number;
  longitude?: number;
  startTime?: string;
  scoresAnnouncedTime?: string;
  timezone?: string;
  isExhibition: boolean;
  corpsIds: string[];
  schedule: ShowPrefillScheduleEntry[];
  date?: string;
}

export interface Show {
  id: string;
  name: string;
  url?: string;
  date: string;
  startTime?: string;
  scoresAnnouncedTime?: string;
  timezone?: string;
  isExhibition: boolean;
  location?: string;
  latitude?: number;
  longitude?: number;
  corpsIds: string[];
  scrapeStatus: 'NotStarted' | 'Succeeded' | 'Failed';
  lastScrapeAttemptAt?: string;
  scrapeError?: string;
  noScoreReason: string | null;
  schedule: ShowScheduleEntry[];
}

export interface TriggerScrapeResult {
  outcome: 'Succeeded' | 'Failed' | 'Skipped';
  error: string | null;
}

export interface PickScore {
  corpsName: string;
  score: number | null;
  iconUrl?: string;
}

export interface CaptionBreakdown {
  avg: number;
  picks: PickScore[];
}

export interface MemberScoreBreakdown {
  userId: string;
  displayName: string;
  totalScore: number;
  captions: Partial<Record<ComputedCaption, CaptionBreakdown>>;
}

export type Caption =
  | 'GeneralEffect'
  | 'GeneralEffectMusic'
  | 'GeneralEffectVisual'
  | 'Visual'
  | 'VisualAnalysis'
  | 'VisualProficiency'
  | 'ColorGuard'
  | 'Music'
  | 'Brass'
  | 'MusicAnalysis'
  | 'Percussion'
  | 'SubTotal'
  | 'Penalty'
  | 'Total'
  | 'VisualPerformance';

export interface DciSeason {
  id: string;
  year: number;
}

export interface DciStandingsShowRef {
  showName: string;
  date: string;
  score: number;
}

export interface DciStandingsEntry {
  corpsId: string;
  corpsName: string;
  corpsIconUrl?: string;
  latest: DciStandingsShowRef;
  last3: DciStandingsShowRef[];
  last3Avg: number;
}

export interface DciScheduleEntry {
  time: string | null;
  label: string;
  corpsId: string | null;
  corpsName: string | null;
}

export interface DciScheduleShow {
  id: string;
  name: string;
  date: string;
  startTime?: string;
  timezone?: string;
  location?: string;
  isExhibition: boolean;
  schedule: DciScheduleEntry[];
}

export interface DciScoreResult {
  rank: number;
  corpsId: string;
  corpsName: string;
  totalScore: number;
}

export interface DciScoresShow {
  id: string;
  name: string;
  date: string;
  isExhibition: boolean;
  noScoreReason: string | null;
  scoresPending: boolean;
  results: DciScoreResult[];
}

export interface DciRecapScoreRow {
  caption: Caption;
  judge: string | null;
  repertoireScore: number;
  performanceScore: number;
  totalScore: number;
}

export interface DciRecapCorpsEntry {
  corpsId: string;
  corpsName: string;
  corpsIconUrl?: string;
  scores: DciRecapScoreRow[];
}

export interface DciRecapShow {
  id: string;
  name: string;
  date: string;
  location?: string;
}

export interface DciRecapResponse {
  show: DciRecapShow;
  corps: DciRecapCorpsEntry[];
}
