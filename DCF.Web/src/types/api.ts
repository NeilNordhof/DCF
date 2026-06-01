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
}

export interface CreateLeagueRequest {
  name: string;
  isPublic: boolean;
  corpsPerCaption: number;
  maxPlayers: number;
  draftableCaptions: ComputedCaption[];
  draftStartTime?: string | null;
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

export interface Show {
  id: string;
  name: string;
  url: string;
  date: string;
  startTime?: string;
  scoresAnnouncedTime: string;
  timezone?: string;
  corpsIds: string[];
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
