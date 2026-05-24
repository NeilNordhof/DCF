export type DraftStatus = 'NotStarted' | 'Scheduled' | 'Open' | 'InProgress' | 'Completed';

export interface League {
  id: string;
  name: string;
  isPublic: boolean;
  inviteCode?: string;
  commissionerUserId?: string;
  draftStatus: DraftStatus;
  draftStartTime?: string;
  corpsPerCaption: number;
  draftableCaptions: string[];
  seasonYear: number;
  isMember?: boolean;
  memberCount?: number;
  members?: Member[];
  picks?: DraftPick[];
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
  caption: string;
  pickNumber: number;
  roundNumber: number;
}

export interface Standing {
  userId: string;
  displayName: string;
  score: number;
}

export interface Corps {
  id: string;
  name: string;
}

export interface DraftState {
  status: DraftStatus;
  draftStartTime?: string;
  currentPickNumber: number;
  currentDrafterId?: string;
  draftOrder: { userId: string; displayName: string }[];
  members: Member[];
  picks: DraftPick[];
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
  draftableCaptions: string[];
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
}

export interface Show {
  id: string;
  name: string;
  url: string;
  date: string;
  scoresAnnouncedTime: string;
  corpsIds: string[];
}
