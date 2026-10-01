import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged, forkJoin, of, Subject, switchMap, timer } from 'rxjs';

interface ProjectSummary {
  id: number;
  name: string;
  primaryPath: string;
  reportCount: number;
  openCount: number;
  lastReportAt: string | null;
  aiAccessGranted: boolean;
}

interface ProjectAiAccessResult {
  projectId: number;
  granted: boolean;
}

interface Note {
  id: string;
  project: string;
  title: string;
  location: string | null;
  observation: string;
  impact: string | null;
  suggestedAction: string | null;
  discoveredDuring: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

interface NotePage {
  items: Note[];
  total: number;
  nextBefore: number | null;
}

interface NoteWorkflowInfo {
  reportId: string;
  noteState: 'new' | 'processed' | 'rejected';
  reviewState: 'queued' | 'checking' | 'awaiting_decision' | 'processed' | 'rejected';
  relevance: 'current' | 'obsolete' | 'unverified' | null;
  recommendedAction: 'create_task' | 'reject' | null;
  finalRejectionReason: string | null;
  reason: string | null;
  submittedRejectionReason: string | null;
  agentVerification: string | null;
  updatedAt: string;
}

interface TaskProposal {
  id: number;
  reportId: string;
  projectId: number;
  reportProject: string;
  state: 'queued' | 'processing' | 'ready' | 'unverified' | 'failed' | 'confirmed' | 'rejected';
  reviewIntent: 'proposal' | 'rejection';
  requestedRejectionReason: string | null;
  requestedClarification: string | null;
  relevance: 'current' | 'obsolete' | 'unverified' | null;
  recommendedAction: 'create_task' | 'reject' | null;
  finalRejectionReason: string | null;
  verificationSummary: string | null;
  title: string | null;
  description: string | null;
  acceptanceCriteria: string | null;
  error: string | null;
  attempts: number;
  createdAt: string;
  updatedAt: string;
  reportTitle: string;
  reportLocation: string | null;
  reportObservation: string;
  reportImpact: string | null;
  reportSuggestedAction: string | null;
  reportDiscoveredDuring: string | null;
  localizedReportTitle: string | null;
  localizedReportObservation: string | null;
  localizedReportImpact: string | null;
  localizedReportSuggestedAction: string | null;
}

interface AutomationStatus {
  available: boolean;
  enabled: boolean;
}

interface RestoreResult {
  workflow: NoteWorkflowInfo;
  proposal: TaskProposal;
}

interface ReprocessResult extends RestoreResult {
  deletedTaskId: string | null;
}

interface NoteCounts {
  all: number;
  queued: number;
  checking: number;
  awaiting_decision: number;
  processed: number;
  rejected: number;
}

interface WorkTask {
  id: string;
  projectId: number;
  title: string;
  description: string;
  acceptanceCriteria: string;
  status: 'todo' | 'done';
  createdAt: string;
  updatedAt: string;
  sourceReportIds: string[];
}

type Workspace = 'notes' | 'tasks';

const noteStatusOptions = [
  { value: '', label: 'Все' },
  { value: 'queued', label: 'В очереди' },
  { value: 'checking', label: 'Проверяется агентом' },
  { value: 'awaiting_decision', label: 'К ревью человеком' },
  { value: 'processed', label: 'Обработана' },
  { value: 'rejected', label: 'Отклонена' },
] as const;

const taskStatusOptions = [
  { value: '', label: 'Все' },
  { value: 'todo', label: 'К выполнению' },
  { value: 'done', label: 'Выполненные' },
] as const;

@Component({
  imports: [CommonModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchChanges = new Subject<string>();
  private requestVersion = 0;

  protected readonly projects = signal<ProjectSummary[]>([]);
  protected readonly notes = signal<Note[]>([]);
  protected readonly tasks = signal<WorkTask[]>([]);
  protected readonly proposal = signal<TaskProposal | null>(null);
  protected readonly selectedProposal = signal<TaskProposal | null>(null);
  protected readonly workflows = signal<Record<string, NoteWorkflowInfo>>({});
  protected readonly automation = signal<AutomationStatus | null>(null);
  protected readonly selectedNote = signal<Note | null>(null);
  protected readonly selectedWorkflow = signal<NoteWorkflowInfo | null>(null);
  protected readonly selectedTask = signal<WorkTask | null>(null);
  protected readonly selectedProjectId = signal<number | null>(null);
  protected readonly workspace = signal<Workspace>('notes');
  protected readonly selectedNoteStatus = signal('');
  protected readonly selectedTaskStatus = signal('');
  protected readonly searchText = signal('');
  protected readonly projectSearchText = signal('');
  protected readonly showOtherProjects = signal(false);
  protected readonly focusMode = signal(false);
  protected readonly sidebarCollapsed = signal(false);
  protected readonly verificationExpanded = signal(false);
  protected readonly rejectionFormOpen = signal(false);
  protected readonly rejectionReason = signal('');
  protected readonly clarificationFormOpen = signal(false);
  protected readonly clarificationInstruction = signal('');
  protected readonly reprocessConfirmationOpen = signal(false);
  protected readonly noteCounts = signal<NoteCounts>({ all: 0, queued: 0, checking: 0, awaiting_decision: 0, processed: 0, rejected: 0 });
  protected readonly total = signal(0);
  protected readonly nextBefore = signal<number | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly actionPending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly noteStatusOptions = noteStatusOptions;
  protected readonly taskStatusOptions = taskStatusOptions;

  protected readonly allNoteCount = computed(() =>
    this.projects().reduce((sum, project) => sum + project.reportCount, 0),
  );
  protected readonly todoCount = computed(() =>
    this.tasks().filter((task) => task.status === 'todo').length,
  );
  protected readonly selectedProject = computed(() =>
    this.projects().find((project) => project.id === this.selectedProjectId()) ?? null,
  );
  protected readonly primaryProjects = computed(() =>
    this.filteredProjects().filter((project) => project.reportCount > 1),
  );
  protected readonly otherProjects = computed(() =>
    this.filteredProjects().filter((project) => project.reportCount <= 1),
  );
  protected readonly filteredTasks = computed(() => {
    const search = this.searchText().toLocaleLowerCase('ru');
    return this.tasks().filter((task) => {
      const projectMatches =
        this.selectedProjectId() === null || task.projectId === this.selectedProjectId();
      const statusMatches = !this.selectedTaskStatus() || task.status === this.selectedTaskStatus();
      const searchMatches =
        !search ||
        task.title.toLocaleLowerCase('ru').includes(search) ||
        task.description.toLocaleLowerCase('ru').includes(search) ||
        task.acceptanceCriteria.toLocaleLowerCase('ru').includes(search);
      return projectMatches && statusMatches && searchMatches;
    });
  });

  constructor() {
    this.searchChanges
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.workspace() === 'notes') {
          this.loadNotes(true);
        }
      });

    timer(0, 4_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.loadProposal();
        this.loadAutomation();
        this.loadVisibleWorkflows();
        this.loadSelectedReview();
      });

    this.loadProjects();
    this.loadNotes(true);
    this.loadTasks();
    this.loadNoteCounts();
  }

  protected switchWorkspace(workspace: Workspace): void {
    this.workspace.set(workspace);
    this.searchText.set('');
    this.notice.set(null);
    if (workspace === 'notes') {
      this.loadNotes(true);
    } else {
      this.loadTasks();
    }
  }

  protected selectProject(projectId: number | null): void {
    if (this.selectedProjectId() === projectId) {
      return;
    }

    this.selectedProjectId.set(projectId);
    this.loadNoteCounts();
    if (this.workspace() === 'notes') {
      this.loadNotes(true);
    }
  }

  protected selectNoteStatus(status: string): void {
    if (this.selectedNoteStatus() !== status) {
      this.selectedNoteStatus.set(status);
      this.loadNotes(true);
    }
  }

  protected selectTaskStatus(status: string): void {
    this.selectedTaskStatus.set(status);
    this.selectedTask.set(this.filteredTasks()[0] ?? null);
  }

  protected updateSearch(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchText.set(value.trim());
    this.searchChanges.next(value.trim());
  }

  protected updateProjectSearch(event: Event): void {
    this.projectSearchText.set((event.target as HTMLInputElement).value.trim());
  }

  protected selectNote(note: Note): void {
    this.selectedNote.set(note);
    this.selectedWorkflow.set(null);
    this.selectedProposal.set(null);
    this.rejectionFormOpen.set(false);
    this.rejectionReason.set('');
    this.clarificationFormOpen.set(false);
    this.clarificationInstruction.set('');
    this.reprocessConfirmationOpen.set(false);
    this.loadSelectedReview(note.id);
  }

  private loadSelectedReview(noteId = this.selectedNote()?.id): void {
    if (!noteId) return;

    forkJoin({
      workflow: this.http.get<NoteWorkflowInfo>(`/api/notes/${noteId}/workflow`),
      proposal: this.http.get<TaskProposal | null>(`/api/notes/${noteId}/proposal`),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ workflow, proposal }) => {
          if (this.selectedNote()?.id !== noteId) return;
          this.selectedWorkflow.set(workflow);
          this.selectedProposal.set(proposal);
          this.workflows.update((workflows) => ({ ...workflows, [noteId]: workflow }));
        },
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  protected selectTask(task: WorkTask): void {
    this.selectedTask.set(task);
  }

  protected loadMore(): void {
    if (this.nextBefore() !== null && !this.loadingMore()) {
      this.loadNotes(false);
    }
  }

  protected noteState(note: Note): 'new' | 'processed' | 'rejected' {
    if (note.status === 'dismissed') return 'rejected';
    return note.status === 'resolved' ? 'processed' : 'new';
  }

  protected reviewState(note: Note): NoteWorkflowInfo['reviewState'] {
    const workflow = this.workflows()[note.id];
    if (workflow) return workflow.reviewState;
    const state = this.noteState(note);
    return state === 'processed' ? 'processed' : state === 'rejected' ? 'rejected' : 'queued';
  }

  protected reviewStateLabel(state: NoteWorkflowInfo['reviewState']): string {
    if (state === 'queued') return 'В очереди на обработку';
    if (state === 'checking') return 'Проверяется агентом';
    if (state === 'awaiting_decision') return 'К ревью человеком';
    if (state === 'processed') return 'Обработана';
    return 'Отклонена';
  }

  protected taskStatusLabel(task: WorkTask): string {
    return task.status === 'done' ? 'Выполнена' : 'К выполнению';
  }

  protected projectName(projectId: number): string {
    const project = this.projects().find((candidate) => candidate.id === projectId);
    return project ? this.projectDisplayName(project) : `Проект ${projectId}`;
  }

  protected projectDisplayName(project: ProjectSummary): string {
    const parts = project.name.replace(/[\\/]+$/, '').split(/[\\/]/).filter(Boolean);
    return parts.at(-1) ?? project.name;
  }

  protected projectLabel(note: Note): string {
    const project = this.projects().find(
      (candidate) => candidate.primaryPath.toLocaleLowerCase() === note.project.toLocaleLowerCase(),
    );
    return project
      ? this.projectDisplayName(project)
      : note.project.split(/[\\/]/).filter(Boolean).at(-1) ?? note.project;
  }

  protected formatDate(value: string): string {
    return new Intl.DateTimeFormat('ru-RU', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(new Date(value));
  }

  protected copyId(id: string): void {
    void navigator.clipboard?.writeText(id);
    this.notice.set(`${id} скопирован`);
  }

  protected copyText(value: string, message: string): void {
    void navigator.clipboard?.writeText(value);
    this.notice.set(message);
  }

  protected openProject(note: Note): void {
    window.location.href = `vscode://file/${note.project.replaceAll('\\', '/')}`;
  }

  protected canRetryNote(note: Note): boolean {
    const proposal = this.selectedProposal();
    return proposal?.reportId === note.id && ['failed', 'unverified', 'ready'].includes(proposal.state);
  }

  protected canRejectNote(note: Note): boolean {
    const state = this.reviewState(note);
    return state !== 'processed' && state !== 'rejected' && state !== 'checking';
  }

  protected updateRejectionReason(event: Event): void {
    this.rejectionReason.set((event.target as HTMLTextAreaElement).value);
  }

  protected updateClarificationInstruction(event: Event): void {
    this.clarificationInstruction.set((event.target as HTMLTextAreaElement).value);
  }

  protected confirmProposal(): void {
    const proposal = this.selectedProposal();
    if (
      !proposal ||
      proposal.state !== 'ready' ||
      proposal.recommendedAction !== 'create_task' ||
      this.actionPending()
    ) {
      return;
    }

    this.actionPending.set(true);
    this.http
      .post<WorkTask>(`/api/task-proposals/${proposal.id}/confirm`, {})
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (task) => {
          this.selectedProposal.set(null);
          this.notice.set(`Задача ${task.id} создана`);
          this.selectedTask.set(task);
          this.refreshNotesReview();
          this.loadTasks(task.id);
          this.actionPending.set(false);
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected confirmAgentRejection(): void {
    const proposal = this.selectedProposal();
    if (
      !proposal ||
      proposal.state !== 'ready' ||
      proposal.recommendedAction !== 'reject' ||
      this.actionPending()
    ) {
      return;
    }

    this.actionPending.set(true);
    this.http
      .post<TaskProposal>(`/api/task-proposals/${proposal.id}/confirm-rejection`, {})
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.selectedProposal.set(updated);
          this.notice.set('Заметка отклонена');
          this.actionPending.set(false);
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected requeueSelected(): void {
    const proposal = this.selectedProposal();
    if (!proposal || proposal.state !== 'processing' || this.actionPending()) {
      return;
    }

    this.actionPending.set(true);
    this.http
      .post<TaskProposal>(`/api/task-proposals/${proposal.id}/requeue`, {})
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.selectedProposal.set(updated);
          this.notice.set('Заметка возвращена в очередь');
          this.actionPending.set(false);
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected retryProposal(): void {
    const proposal = this.selectedProposal();
    if (!proposal || !['failed', 'ready', 'unverified'].includes(proposal.state) || this.actionPending()) {
      return;
    }

    const project = this.projects().find((candidate) => candidate.id === proposal.projectId);
    if (!project?.aiAccessGranted) {
      this.selectProject(proposal.projectId);
      this.notice.set('Сначала разрешите OpenCode чтение этого проекта');
      return;
    }

    const automation = this.automation();
    if (!automation?.available) {
      this.error.set('Обработка OpenCode недоступна');
      return;
    }

    this.actionPending.set(true);
    const startProcessing = automation.enabled
      ? of(automation)
      : this.http.patch<AutomationStatus>('/api/automation', { enabled: true });
    startProcessing
      .pipe(
        switchMap((status) => {
          this.automation.set(status);
          return this.http.post<TaskProposal>(`/api/task-proposals/${proposal.id}/retry`, {});
        }),
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.selectedProposal.set(updated);
          this.notice.set('Повторная проверка запущена');
          this.actionPending.set(false);
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected submitRejection(): void {
    const note = this.selectedNote();
    const reason = this.rejectionReason().trim();
    if (!note || !reason || this.actionPending()) return;

    const project = this.projects().find(
      (candidate) => candidate.primaryPath.toLocaleLowerCase() === note.project.toLocaleLowerCase(),
    );
    if (!project?.aiAccessGranted) {
      if (project) this.selectProject(project.id);
      this.notice.set('Сначала разрешите OpenCode чтение этого проекта');
      return;
    }
    const automation = this.automation();
    if (!automation?.available) {
      this.error.set('Обработка OpenCode недоступна');
      return;
    }

    this.actionPending.set(true);
    const startProcessing = automation.enabled
      ? of(automation)
      : this.http.patch<AutomationStatus>('/api/automation', { enabled: true });
    startProcessing
      .pipe(
        switchMap((status) => {
          this.automation.set(status);
          return this.http.post<TaskProposal>(`/api/notes/${note.id}/rejection-review`, { reason });
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (proposal) => {
          this.selectedProposal.set(proposal);
          this.rejectionFormOpen.set(false);
          this.rejectionReason.set('');
          this.notice.set('Причина отправлена OpenCode на проверку');
          this.actionPending.set(false);
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected editRejectionReason(): void {
    this.rejectionReason.set(
      this.selectedWorkflow()?.submittedRejectionReason ??
        this.selectedProposal()?.requestedRejectionReason ??
        '',
    );
    this.clarificationFormOpen.set(false);
    this.rejectionFormOpen.set(true);
  }

  protected requestClarification(): void {
    const note = this.selectedNote();
    const instruction = this.clarificationInstruction().trim();
    if (!note || !instruction || this.actionPending()) return;

    const project = this.projects().find(
      (candidate) => candidate.primaryPath.toLocaleLowerCase() === note.project.toLocaleLowerCase(),
    );
    if (!project?.aiAccessGranted) {
      if (project) this.selectProject(project.id);
      this.notice.set('Сначала разрешите OpenCode чтение этого проекта');
      return;
    }

    this.actionPending.set(true);
    this.http
      .post<TaskProposal>(`/api/notes/${note.id}/clarification`, { instruction })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (proposal) => {
          this.selectedProposal.set(proposal);
          this.clarificationFormOpen.set(false);
          this.clarificationInstruction.set('');
          this.notice.set('Заметка возвращена в очередь на уточнение');
          this.actionPending.set(false);
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected restoreNote(): void {
    const note = this.selectedNote();
    if (!note || this.actionPending()) return;

    this.actionPending.set(true);
    this.http
      .post<RestoreResult>(`/api/notes/${note.id}/restore`, {})
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ workflow, proposal }) => {
          this.selectedWorkflow.set(workflow);
          this.selectedProposal.set(proposal);
          this.notice.set('Заметка возвращена в очередь на обработку');
          this.actionPending.set(false);
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected reprocessCompletedNote(): void {
    const note = this.selectedNote();
    if (!note || this.reviewState(note) !== 'processed' || this.actionPending()) return;

    this.actionPending.set(true);
    this.http
      .post<ReprocessResult>(`/api/notes/${note.id}/reprocess`, {})
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ workflow, proposal, deletedTaskId }) => {
          this.selectedWorkflow.set(workflow);
          this.selectedProposal.set(proposal);
          this.reprocessConfirmationOpen.set(false);
          this.notice.set(deletedTaskId
            ? `Задача ${deletedTaskId} удалена, заметка возвращена в очередь`
            : 'Заметка возвращена в очередь');
          this.actionPending.set(false);
          this.loadTasks();
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected toggleTask(task: WorkTask): void {
    if (this.actionPending()) {
      return;
    }

    const status = task.status === 'todo' ? 'done' : 'todo';
    this.actionPending.set(true);
    this.http
      .patch<WorkTask>(`/api/tasks/${task.id}/status`, { status })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.tasks.update((tasks) => tasks.map((item) => (item.id === updated.id ? updated : item)));
          this.selectedTask.set(updated);
          this.notice.set(status === 'done' ? 'Задача выполнена' : 'Задача возвращена в работу');
          this.actionPending.set(false);
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected toggleAutomation(): void {
    const automation = this.automation();
    if (!automation?.available || this.actionPending()) {
      return;
    }

    const enabled = !automation.enabled;
    this.actionPending.set(true);
    this.http
      .patch<AutomationStatus>('/api/automation', { enabled })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => {
          this.automation.set(status);
          this.notice.set(enabled ? 'Фоновая обработка запущена' : 'Фоновая обработка остановлена');
          this.actionPending.set(false);
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected setProjectAiAccess(project: ProjectSummary, granted: boolean): void {
    this.actionPending.set(true);
    this.error.set(null);
    this.http
      .patch<ProjectAiAccessResult>(`/api/projects/${project.id}/ai-access`, { granted })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.projects.update((projects) =>
            projects.map((candidate) =>
              candidate.id === project.id ? { ...candidate, aiAccessGranted: granted } : candidate,
            ),
          );
          this.notice.set(
            granted
              ? `OpenCode получил доступ к ${this.projectDisplayName(project)}`
              : `Доступ OpenCode к ${this.projectDisplayName(project)} отозван`,
          );
          this.actionPending.set(false);
          this.loadProposal();
        },
        error: (error: HttpErrorResponse) => {
          this.actionPending.set(false);
          this.setError(error);
        },
      });
  }

  private loadProjects(): void {
    this.http
      .get<ProjectSummary[]>('/api/projects')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (projects) => this.projects.set(projects),
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  private filteredProjects(): ProjectSummary[] {
    const search = this.projectSearchText().toLocaleLowerCase('ru');
    if (!search) return this.projects();
    return this.projects().filter(
      (project) =>
        this.projectDisplayName(project).toLocaleLowerCase('ru').includes(search) ||
        project.primaryPath.toLocaleLowerCase('ru').includes(search),
    );
  }

  private loadNoteCounts(): void {
    let baseParams = new HttpParams().set('limit', 1);
    if (this.selectedProjectId() !== null) {
      baseParams = baseParams.set('projectId', this.selectedProjectId()!);
    }

    const page = (reviewState?: string) =>
      this.http.get<NotePage>('/api/notes', {
        params: reviewState ? baseParams.set('reviewState', reviewState) : baseParams,
      });

    forkJoin({
      all: page(),
      queued: page('queued'),
      checking: page('checking'),
      awaiting_decision: page('awaiting_decision'),
      processed: page('processed'),
      rejected: page('rejected'),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (counts) =>
          this.noteCounts.set({
            all: counts.all.total,
            queued: counts.queued.total,
            checking: counts.checking.total,
            awaiting_decision: counts.awaiting_decision.total,
            processed: counts.processed.total,
            rejected: counts.rejected.total,
          }),
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  private loadNotes(reset: boolean): void {
    const requestVersion = ++this.requestVersion;
    const cursor = reset ? null : this.nextBefore();
    if (!reset && cursor === null) {
      return;
    }

    reset ? this.loading.set(true) : this.loadingMore.set(true);
    let params = new HttpParams().set('limit', 40);
    if (this.selectedProjectId() !== null) {
      params = params.set('projectId', this.selectedProjectId()!);
    }
    if (this.selectedNoteStatus()) {
      params = params.set('reviewState', this.selectedNoteStatus());
    }
    if (this.searchText()) {
      params = params.set('query', this.searchText());
    }
    if (cursor !== null) {
      params = params.set('before', cursor);
    }

    this.http
      .get<NotePage>('/api/notes', { params })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          if (requestVersion !== this.requestVersion) return;
          const selectedId = this.selectedNote()?.id;
          const visibleNotes = reset ? page.items : [...this.notes(), ...page.items];
          this.notes.set(visibleNotes);
          this.total.set(page.total);
          this.nextBefore.set(page.nextBefore);
          this.loadVisibleWorkflows();
          if (reset) {
            const selected = visibleNotes.find((note) => note.id === selectedId);
            const nextSelection = selected ?? page.items[0] ?? null;
            if (nextSelection) this.selectNote(nextSelection);
            else {
              this.selectedNote.set(null);
              this.selectedWorkflow.set(null);
              this.selectedProposal.set(null);
            }
          }
          this.finishLoading();
        },
        error: (error: HttpErrorResponse) => {
          if (requestVersion !== this.requestVersion) return;
          this.setError(error);
          this.finishLoading();
        },
      });
  }

  private loadVisibleWorkflows(): void {
    const ids = this.notes().map((note) => note.id);
    if (ids.length === 0) {
      this.workflows.set({});
      return;
    }

    this.http
      .post<NoteWorkflowInfo[]>('/api/notes/workflows', { ids })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (workflows) =>
          this.workflows.set(
            Object.fromEntries(workflows.map((workflow) => [workflow.reportId, workflow])),
          ),
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  private refreshNotesReview(): void {
    this.loadProjects();
    this.loadNotes(true);
    this.loadNoteCounts();
    this.loadProposal();
    this.loadSelectedReview();
  }

  private loadTasks(selectId?: string): void {
    this.http
      .get<WorkTask[]>('/api/tasks')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (tasks) => {
          this.tasks.set(tasks);
          const selected = selectId ? tasks.find((task) => task.id === selectId) : null;
          this.selectedTask.set(selected ?? this.selectedTask() ?? tasks[0] ?? null);
        },
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  private loadProposal(): void {
    this.http
      .get<TaskProposal | null>('/api/task-proposals/current')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (proposal) => this.proposal.set(proposal),
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  private loadAutomation(): void {
    this.http
      .get<AutomationStatus>('/api/automation')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => this.automation.set(status),
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  private finishActionWithError(error: HttpErrorResponse): void {
    this.actionPending.set(false);
    this.setError(error);
  }

  private setError(error: HttpErrorResponse): void {
    this.error.set(error.error?.error ?? 'Не удалось получить данные. Проверьте локальный сервер.');
  }

  private finishLoading(): void {
    this.loading.set(false);
    this.loadingMore.set(false);
  }
}
