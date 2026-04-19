export type DraftVariant = {
  id: number;
  shape: string;
  shapeLabel: string;
  confidenceScore: number;
  body: string;
  groundingWarning?: string | null;
};

export type EmailAsk = {
  text: string;
  askType: string;
  isOptional: boolean;
};

export type DecisionBranch = {
  summary: string;
  viableReplyShapes: string[];
};

export type EmailAnalysis = {
  asks: EmailAsk[];
  decisionBranches: DecisionBranch[];
  statedDeadlines: string[];
  urgency: string;
};

export type DraftSummary = {
  id: number;
  fromAddress: string;
  subject: string;
  sourceReceivedAt: string;
  draftCreatedAt: string;
  status: string;
  variantCount: number;
  topConfidence: number;
  urgency: string | null;
};

export type DraftDetail = {
  id: number;
  fromAddress: string;
  subject: string;
  sourceReceivedAt: string;
  draftCreatedAt: string;
  status: string;
  variants: DraftVariant[];
  sourceMessageId: string;
  selectedVariantId?: number | null;
  analysis?: EmailAnalysis | null;
};

export type SkippedEmail = {
  id: number;
  fromAddress: string;
  subject: string;
  receivedAt: string;
  reasonCode: string;
};

export type RunRecord = {
  id: number;
  startedAt: string;
  candidatesEvaluated: number;
  skippedCount: number;
  draftCreated: boolean;
  draftId?: number | null;
  skipBuckets: Record<string, number>;
};
