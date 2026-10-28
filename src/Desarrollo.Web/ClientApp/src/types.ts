export type Priority = 0 | 1 | 2 | 3;
export type WorkItemType = 0 | 1 | 2 | 3 | 4 | 5;
export type TicketStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9;

export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number; }

export interface DiagramTaskDto {
  id: string; code: string; title: string; displayTitle: string; parentTaskId?: string;
  description?: string; notes?: string; priority: number;
  stateId: string; stateName: string; stateColor?: string;
  progressPercent?: number | null; accentColor: string; depth: number; x: number; y: number;
  type: WorkItemType; assigneeId?: string; assigneeName?: string;
  rowVersion: string; positionRowVersion: string | null; checklistProgress?: number | null;
  fullPath: string; isArchived: boolean;
  canEdit: boolean; canCreateChild: boolean; canArchive: boolean; canChangeState: boolean;
}
export interface TaskDiagramDto {
  tasks: DiagramTaskDto[]; viewportX: number; viewportY: number; zoom: number;
  projectName: string; permissions: number[]; states: WorkflowStateDto[]; collapsedTaskIds: string[];
}
export interface DiagramPositionDto { taskId: string; x: number; y: number; rowVersion: string | null; }
export interface ArchivePreviewDto { count: number; tasks: { id: string; code: string; title: string }[]; canArchive: boolean; }

export interface CurrentUser {
  id: string | null;
  name: string;
  email: string | null;
  organizationId: string | null;
  organizationName: string | null;
  isAuthenticated: boolean;
}

export interface OrganizationDto { id: string; name: string; slug: string; timeZoneId?: string; }
export interface MemberDto { id: string; userId: string; name: string; isActive: boolean; roleName?: string; email?: string; roleId?: string; }
export interface RoleDto { id: string; name: string; description: string; isSystem: boolean; permissions: number[]; }
export interface ProductDto { id: string; name: string; description?: string; isActive: boolean; }

export interface ProjectDto {
  id: string; code: string; name: string; objective?: string;
  status: number; priority: number; leaderId?: string; dueDate?: string;
  progress?: number | null; isPrivate: boolean;
}
export interface ProjectDetailDto {
  summary: ProjectDto; scope?: string; startDate?: string; risks?: string; links?: string;
  members: MemberDto[]; epics: EpicDto[]; cycles: CycleDto[]; milestones: MilestoneDto[]; releases: ReleaseDto[];
}
export interface EpicDto { id: string; title: string; goal?: string; ownerId?: string; priority: number; dueDate?: string; }
export interface CycleDto { id: string; name: string; goal?: string; startDate: string; endDate: string; isActive: boolean; }
export interface MilestoneDto { id: string; title: string; dueDate?: string; }
export interface ReleaseDto { id: string; name: string; plannedDate?: string; releasedAt?: string; releaseNotes?: string; environments?: string; }

export interface WorkflowStateDto { id: string; name: string; category: number; order: number; color?: string; isDefault: boolean; }

export interface WorkItemCardDto {
  id: string; code: string; title: string; type: WorkItemType;
  assigneeId?: string; assigneeName?: string;
  priority: Priority; stateId: string; stateName: string; stateColor?: string;
  dueDate?: string; isBlocked: boolean; checklistProgress?: number | null; labels: string[];
  projectId?: string; projectCode?: string;
  rowVersion: string;
}
export interface WorkItemDetailDto {
  id: string; code: string; title: string; type: WorkItemType;
  description?: string; objective?: string; context?: string;
  authorId: string; assigneeId?: string; assigneeName?: string;
  collaborators: string[]; reviewers: string[]; followers: string[];
  priority: Priority; startDate?: string; dueDate?: string; estimateHours?: number;
  cycleId?: string; epicId?: string; releaseId?: string;
  stateId: string; stateName: string; category: number;
  isBlocked: boolean; blockReason?: string; blockedByWorkItemId?: string;
  acceptanceCriteria?: string; labels: string[];
  reproSteps?: string; expectedResult?: string; actualResult?: string;
  environment?: string; affectedVersion?: string; severity?: number;
  researchQuestion?: string; researchConclusion?: string;
  projectId?: string; projectCode?: string; organizationId: string;
  createdAt: string; updatedAt: string;
  notes?: string | null; progressPercent?: number | null; accentColor?: string | null; parentTaskId?: string | null;
  rowVersion: string; fullPath: string; isArchived: boolean;
}
export interface ChecklistItemDto { id: string; text: string; isCompleted: boolean; assigneeId?: string; completedBy?: string; order: number; }
export interface ChecklistDto { id: string; title: string; order: number; items: ChecklistItemDto[]; }
export interface CommentDto { id: string; authorId: string; authorName?: string; body: string; createdAt: string; }

export interface TicketDto {
  id: string; number: number; code: string; title: string;
  requesterId?: string; requesterName?: string; origin: number;
  category: number; severity: number; priority: number;
  projectId?: string; projectCode?: string; assigneeId?: string;
  status: TicketStatus; affectedVersion?: string; environment?: string;
  resolvedAt?: string; createdAt: string;
  canEdit?: boolean; canAssign?: boolean; canChangeStatus?: boolean; canComment?: boolean;
  canReadInternalNotes?: boolean; canWriteInternalNotes?: boolean; canLinkWorkItems?: boolean;
}
export interface TicketCommentDto { id: string; authorId: string; authorName?: string; body: string; isInternal: boolean; createdAt: string; }

export interface DocumentDto {
  id: string; title: string; parentId?: string | null; authorId: string; tags: string[]; updatedAt: string; content?: string | null;
  projectId?: string; visibility?: 0 | 1 | 2; readerIds?: string[]; canEdit?: boolean; canManageAccess?: boolean;
  contentFormat?: 'plain' | 'tiptap'; rowVersion?: string;
}
export interface StoredFileDto {
  id: string; storedFileId: string; code: string; name: string; contentType: string; sizeBytes: number;
  description?: string; projectId?: string; visibility: 'Public' | 'Private' | 'Context'; entityType?: string; entityId?: string;
  uploaderId: string; createdAt: string; canDelete: boolean;
}
export interface ChannelDto { id: string; name: string; topic?: string; type: number; projectId?: string; projectCode?: string; isMember: boolean; unreadCount?: number; directUserId?: string; }
export interface MessageDto { id: string; channelId: string; authorId: string; authorName?: string; threadRootId?: string; body: string; isEdited: boolean; createdAt: string; }
export interface NotificationDto {
  id: string; event: number; message: string; entityType: string; entityId: string;
  route?: string; isRead: boolean; createdAt: string; snoozedUntil?: string;
}
export interface CalendarEventDto {
  id: string; title: string; description?: string; type: number; isAllDay: boolean;
  start: string; end: string; timeZoneId?: string; recurrence?: number;
  linkedWorkItemId?: string; linkedMilestoneId?: string; linkedReleaseId?: string;
  projectId?: string; projectCode?: string;
  priority?: number | null; color?: string | null; assigneeId?: string; assigneeName?: string;
  sourceType?: string; sourceId?: string | null; dateKind?: string | null; sourceRoute?: string | null;
  isDerived?: boolean; canEdit?: boolean; canDelete?: boolean;
}
export interface ReportSnapshotDto {
  totalTasks: number; doneTasks: number; overdueTasks: number; blockedTasks: number;
  openTickets: number; medianCycleDays: number; wipLimit: number; byState: { key: string; label: string; count: number }[];
}
