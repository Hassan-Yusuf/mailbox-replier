export type DraftVariant = {
  id: number;
  shape: string;
  shapeLabel: string;
  confidenceScore: number;
  body: string;
  groundingWarning?: string | null;
  coverageWarning?: string | null;
  wasSelected?: boolean;
  wasEdited?: boolean;
  editedBody?: string | null;
  editDistance?: number | null;
  editedAtUtc?: string | null;
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

export type ConfidenceTier = "Low" | "Medium" | "High";

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
  aggregateConfidence?: number | null;
  tier?: ConfidenceTier | null;
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
  aggregateConfidence?: number | null;
  tier?: ConfidenceTier | null;
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

export type WorkflowMode = "ReviewBeforeSend" | "SuggestOnly";

export type WorkflowConfig = {
  mode: WorkflowMode;
};

export type PolicyRule = {
  ruleId: string;
  displayName: string;
  description: string;
  category: string;
  defaultEnabled: boolean;
  isUserConfigurable: boolean;
  currentlyEnabled: boolean;
};
