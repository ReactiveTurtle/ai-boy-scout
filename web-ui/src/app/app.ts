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

interface SetupPreview {
  repository: string;
  mcpDll: string;
  siteUrl: string;
  files: { path: string; action: string }[];
  warning: string;
  token: string;
}

interface Note {
  id: string;
  project: string;
  projectId: number | null;
  title: string;
  location: string | null;
  observation: string;
  impact: string | null;
  suggestedAction: string | null;
  discoveredDuring: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
  sourceName: string | null;
  sourceInstallationId: string | null;
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
  agentCheckedAt: string | null;
  humanApprovedAt: string | null;
  humanRejectedAt: string | null;
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
  currentConsequences: string | null;
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
  currentConsequences: string | null;
  acceptanceCriteria: string;
  status: 'todo' | 'done';
  createdAt: string;
  updatedAt: string;
  sourceReportIds: string[];
  confirmerName: string | null;
  confirmerInstallationId: string | null;
}

interface StorageStatus {
  installationId: string;
  displayName: string;
  backend: number;
  hasFirebaseConnection: boolean;
}

interface RemoteProject { id: number; name: string; }
interface RemoteBinding { projectId: number; localPath: string; aiReadConsent: boolean; }

interface TransferPreview {
  projectId: number;
  name: string;
  paths: string[];
  counts: { paths: number; reports: number; proposals: number; tasks: number; reportTaskLinks: number; aiConsent: number };
  sha256: string;
  requiresConfirmation: boolean;
  transferAvailable: boolean;
}

interface TaskPage {
  items: WorkTask[];
  total: number;
  nextBefore: number | null;
}

type Workspace = 'notes' | 'tasks' | 'storage';

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
  private taskRequestVersion = 0;
  private workflowRequestVersion = 0;
  private shortcutHandled = false;

  protected readonly projects = signal<ProjectSummary[]>([]);
  protected readonly notes = signal<Note[]>([]);
  protected readonly tasks = signal<WorkTask[]>([]);
  protected readonly taskTotal = signal(0);
  protected readonly taskNextBefore = signal<number | null>(null);
  protected readonly taskLoading = signal(false);
  protected readonly taskLoadingMore = signal(false);
  protected readonly proposal = signal<TaskProposal | null>(null);
  protected readonly selectedProposal = signal<TaskProposal | null>(null);
  protected readonly workflows = signal<Record<string, NoteWorkflowInfo>>({});
  protected readonly automation = signal<AutomationStatus | null>(null);
  protected readonly selectedNote = signal<Note | null>(null);
  protected readonly selectedWorkflow = signal<NoteWorkflowInfo | null>(null);
  protected readonly selectedTask = signal<WorkTask | null>(null);
  protected readonly taskSourceNames = signal<string[]>([]);
  protected readonly selectedProjectId = signal<number | null>(null);
  protected readonly workspace = signal<Workspace>(window.location.pathname.replace(/\/$/, '') === '/settings/storage' ? 'storage' : 'notes');
  protected readonly selectedNoteStatus = signal('');
  protected readonly selectedTaskStatus = signal('');
  protected readonly searchText = signal('');
  protected readonly projectSearchText = signal('');
  protected readonly projectFormOpen = signal(false);
  protected readonly newProjectName = signal('');
  protected readonly newProjectPath = signal('');
  protected readonly setupOpen = signal(false);
  protected readonly setupPath = signal('');
  protected readonly setupPreview = signal<SetupPreview | null>(null);
  protected readonly setupPending = signal(false);
  protected readonly projectSettingsOpen = signal(false);
  protected readonly storageStatus = signal<StorageStatus | null>(null);
  protected readonly displayName = signal('');
  protected readonly firebaseUrl = signal('');
  protected readonly firebaseProjectId = signal('');
  protected readonly firebaseClientEmail = signal('');
  protected readonly firebasePrivateKey = signal('');
  protected readonly firebaseConnectionState = signal<'idle' | 'checking' | 'connected' | 'failed'>('idle');
  protected readonly storagePending = signal(false);
  protected readonly remoteProjects = signal<RemoteProject[]>([]);
  protected readonly remoteBindings = signal<RemoteBinding[]>([]);
  protected readonly remoteProjectName = signal('');
  protected readonly remotePaths = signal<Record<number, string>>({});
  protected readonly previewProjectId = signal<number | null>(null);
  protected readonly transferPreview = signal<TransferPreview | null>(null);
  protected readonly showOtherProjects = signal(false);
  protected readonly focusMode = signal(false);
  protected readonly sidebarCollapsed = signal(false);
  protected readonly verificationExpanded = signal(false);
  protected readonly rejectionFormOpen = signal(false);
  protected readonly rejectionReason = signal('');
  protected readonly clarificationFormOpen = signal(false);
  protected readonly clarificationInstruction = signal('');
  protected readonly processingComment = signal('');
  protected readonly restoreFormOpen = signal(false);
  protected readonly restoreComment = signal('');
  protected readonly reprocessConfirmationOpen = signal(false);
  protected readonly reprocessComment = signal('');
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

  constructor() {
    this.searchChanges
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.workspace() === 'notes') {
          this.loadNotes(true);
        } else {
          this.loadTasks();
        }
      });

    timer(0, 4_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.workspace() === 'storage') return;
        this.loadProposal();
        this.loadAutomation();
        this.loadVisibleWorkflows();
        this.loadSelectedReview();
      });

    if (this.workspace() !== 'storage') {
      this.loadProjects();
      this.loadNotes(true);
      this.loadTasks();
      this.loadNoteCounts();
    }
    this.loadStorageStatus();
  }

  private loadStorageStatus(): void {
    this.http.get<StorageStatus>('/api/storage')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => { this.storageStatus.set(status); this.displayName.set(status.displayName); },
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  protected toggleProjectSettings(): void {
    if (!this.selectedProject()) return;
    this.projectSettingsOpen.set(!this.projectSettingsOpen());
    if (this.projectSettingsOpen()) {
      this.setupOpen.set(false);
      this.previewProjectId.set(this.selectedProjectId());
      this.transferPreview.set(null);
      if (this.storageStatus()?.hasFirebaseConnection) this.loadRemoteProjects();
    }
  }

  protected selectPreviewProject(value: string): void {
    this.previewProjectId.set(value ? Number(value) : null);
    this.transferPreview.set(null);
  }

  protected showTransferPreview(): void {
    const id = this.previewProjectId() ?? this.selectedProjectId() ?? this.projects()[0]?.id;
    if (!id || this.storagePending()) return;
    this.storagePending.set(true);
    this.http.get<TransferPreview>(`/api/projects/${id}/transfer-preview`)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (preview) => { this.transferPreview.set(preview); this.storagePending.set(false); },
        error: (error: HttpErrorResponse) => { this.storagePending.set(false); this.setError(error); },
      });
  }

  protected remoteBinding(projectId: number): RemoteBinding | null {
    return this.remoteBindings().find((binding) => binding.projectId === projectId) ?? null;
  }

  protected remotePath(projectId: number): string {
    return this.remotePaths()[projectId] ?? this.remoteBinding(projectId)?.localPath ??
      this.selectedProject()?.primaryPath ?? '';
  }

  protected setRemotePath(projectId: number, path: string): void {
    this.remotePaths.update((paths) => ({ ...paths, [projectId]: path }));
  }

  protected loadRemoteProjects(): void {
    if (this.storagePending() || !this.storageStatus()?.hasFirebaseConnection) return;
    this.storagePending.set(true);
    forkJoin({
      projects: this.http.get<RemoteProject[]>('/api/remote-projects'),
      bindings: this.http.get<RemoteBinding[]>('/api/remote-projects/bindings'),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ projects, bindings }) => {
        this.remoteProjects.set(projects);
        this.remoteBindings.set(bindings);
        this.storagePending.set(false);
      },
      error: (error: HttpErrorResponse) => { this.storagePending.set(false); this.setError(error); },
    });
  }

  protected createRemoteProject(): void {
    if (this.storagePending() || !this.remoteProjectName().trim()) return;
    this.storagePending.set(true);
    this.http.post<RemoteProject>('/api/remote-projects', { name: this.remoteProjectName().trim() })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (project) => {
          this.remoteProjects.update((projects) => [...projects, project]);
          this.remoteProjectName.set('');
          this.storagePending.set(false);
          this.notice.set(`Общий проект ${project.name} создан. Привяжите локальный каталог отдельно.`);
        },
        error: (error: HttpErrorResponse) => { this.storagePending.set(false); this.setError(error); },
      });
  }

  protected bindRemoteProject(projectId: number): void {
    const path = this.remotePath(projectId).trim();
    if (this.storagePending() || !path) return;
    this.storagePending.set(true);
    this.http.put<RemoteBinding>(`/api/remote-projects/${projectId}/binding`, { path })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (binding) => {
          this.remoteBindings.update((bindings) => [...bindings.filter((item) => item.projectId !== projectId), binding]);
          this.storagePending.set(false);
          this.notice.set('Локальный путь привязан. Доступ AI по умолчанию выключен.');
        },
        error: (error: HttpErrorResponse) => { this.storagePending.set(false); this.setError(error); },
      });
  }

  protected setRemoteAiConsent(projectId: number, granted: boolean): void {
    if (this.storagePending()) return;
    this.storagePending.set(true);
    this.http.patch<RemoteBinding>(`/api/remote-projects/${projectId}/ai-consent`, { granted })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (binding) => {
          this.remoteBindings.update((bindings) => bindings.map((item) => item.projectId === projectId ? binding : item));
          this.storagePending.set(false);
        },
        error: (error: HttpErrorResponse) => { this.storagePending.set(false); this.setError(error); },
      });
  }

  protected saveDisplayName(): void {
    if (this.storagePending() || !this.displayName().trim()) return;
    this.storagePending.set(true);
    this.http.patch<StorageStatus>('/api/storage/name', { name: this.displayName().trim() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => {
          this.storageStatus.set(status);
          this.storagePending.set(false);
          this.notice.set('Имя BoyScout сохранено для новых заметок и подтверждений задач');
        },
        error: (error: HttpErrorResponse) => { this.storagePending.set(false); this.setError(error); },
      });
  }

  protected saveFirebaseConnection(): void {
    if (this.storagePending() || !this.firebaseUrl().trim() || !this.firebaseProjectId().trim() ||
        !this.firebaseClientEmail().trim() || !this.firebasePrivateKey().trim()) return;
    this.storagePending.set(true);
    this.firebaseConnectionState.set('checking');
    this.error.set(null);
    this.http.post<StorageStatus>('/api/storage/firebase', {
      databaseUrl: this.firebaseUrl().trim(),
      projectId: this.firebaseProjectId().trim(),
      clientEmail: this.firebaseClientEmail().trim(),
      privateKey: this.firebasePrivateKey(),
    }).pipe(
      switchMap((status) => {
        this.storageStatus.set(status);
        return this.http.get<RemoteProject[]>('/api/remote-projects').pipe(
          switchMap((projects) => of({ status, projects })),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: ({ status, projects }) => {
        this.storageStatus.set(status);
        this.remoteProjects.set(projects);
        this.firebasePrivateKey.set('');
        this.storagePending.set(false);
        this.firebaseConnectionState.set('connected');
        this.notice.set('Доступ к Firebase проверен. Общая база готова к настройке проектов.');
      },
      error: (error: HttpErrorResponse) => {
        this.storagePending.set(false);
        this.firebaseConnectionState.set('failed');
        this.setError(error);
      },
    });
  }

  protected openSetup(): void {
    if (!this.selectedProject()) return;
    this.setupPath.set(this.selectedProject()?.primaryPath ?? '');
    this.setupPreview.set(null);
    this.projectSettingsOpen.set(false);
    this.setupOpen.set(true);
  }

  protected changeSetupPath(value: string): void {
    this.setupPath.set(value);
    this.setupPreview.set(null);
  }

  protected previewSetup(): void {
    if (!this.setupPath().trim() || this.setupPending()) return;
    this.setupPending.set(true);
    this.error.set(null);
    this.setupPreview.set(null);
    this.http.post<SetupPreview>('/api/setup/preview', { path: this.setupPath().trim() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => { this.setupPreview.set(preview); this.setupPending.set(false); },
        error: (error: HttpErrorResponse) => { this.setupPending.set(false); this.setError(error); },
      });
  }

  protected confirmSetup(): void {
    const preview = this.setupPreview();
    if (!preview || this.setupPending()) return;
    this.setupPending.set(true);
    this.http.post<SetupPreview>('/api/setup/confirm', { path: preview.repository, token: preview.token })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => { this.setupPreview.set(null); this.setupPending.set(false); this.loadProjects(); this.notice.set('Локальная настройка создана. Запустите общий сайт перед открытием ярлыка.'); },
        error: (error: HttpErrorResponse) => { this.setupPreview.set(null); this.setupPending.set(false); this.setError(error); },
      });
  }

  protected switchWorkspace(workspace: 'notes' | 'tasks'): void {
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
    this.projectSettingsOpen.set(false);
    this.loadNoteCounts();
    if (this.workspace() === 'notes') {
      this.loadNotes(true);
    } else {
      this.loadTasks();
    }
  }

  protected selectNoteStatus(status: string): void {
    if (this.selectedNoteStatus() !== status) {
      this.selectedNoteStatus.set(status);
      this.loadNotes(true);
    }
  }

  protected selectTaskStatus(status: string): void {
    if (this.selectedTaskStatus() !== status) {
      this.selectedTaskStatus.set(status);
      this.loadTasks();
    }
  }

  protected updateSearch(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchText.set(value.trim());
    this.searchChanges.next(value.trim());
  }

  protected updateProjectSearch(event: Event): void {
    this.projectSearchText.set((event.target as HTMLInputElement).value.trim());
  }

  protected createProject(): void {
    if (this.actionPending() || !this.newProjectName().trim() || !this.newProjectPath().trim()) return;

    this.actionPending.set(true);
    this.http.post<ProjectSummary>('/api/projects', {
      name: this.newProjectName().trim(),
      path: this.newProjectPath().trim(),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (project) => {
          this.projects.update((projects) => [...projects, project]);
          this.projectFormOpen.set(false);
          this.newProjectName.set('');
          this.newProjectPath.set('');
          this.notice.set(`Проект ${project.name} создан`);
          this.actionPending.set(false);
          this.selectProject(project.id);
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected selectNote(note: Note): void {
    if (this.selectedNote()?.id === note.id) {
      this.selectedNote.set(note);
      this.loadSelectedReview(note.id);
      return;
    }
    this.selectedNote.set(note);
    this.selectedWorkflow.set(null);
    this.selectedProposal.set(null);
    this.rejectionFormOpen.set(false);
    this.rejectionReason.set('');
    this.clarificationFormOpen.set(false);
    this.clarificationInstruction.set('');
    this.processingComment.set('');
    this.restoreFormOpen.set(false);
    this.restoreComment.set('');
    this.reprocessConfirmationOpen.set(false);
    this.reprocessComment.set('');
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
    this.loadTaskSources(task);
  }

  private loadTaskSources(task: WorkTask): void {
    this.taskSourceNames.set([]);
    if (task.sourceReportIds.length === 0) return;
    forkJoin(task.sourceReportIds.map((id) => this.http.get<Note>(`/api/reports/${encodeURIComponent(id)}`)))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (notes) => {
          if (this.selectedTask()?.id === task.id) {
            this.taskSourceNames.set(notes.map((note) => note.sourceName).filter((name): name is string => !!name));
          }
        },
        error: (error: HttpErrorResponse) => this.setError(error),
      });
  }

  protected loadMore(): void {
    if (this.nextBefore() !== null && !this.loadingMore()) {
      this.loadNotes(false);
    }
  }

  protected loadMoreTasks(): void {
    if (this.taskNextBefore() !== null && !this.taskLoadingMore() && !this.taskLoading()) {
      this.loadTasks(undefined, false);
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

  protected noteReviewLabel(note: Note): string {
    if (this.reviewState(note) === 'queued' && this.noteProject(note)?.aiAccessGranted === false) {
      return 'Ожидает AI-доступа';
    }
    return this.reviewStateLabel(this.reviewState(note));
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
    const project = this.noteProject(note);
    return project
      ? this.projectDisplayName(project)
      : note.project.split(/[\\/]/).filter(Boolean).at(-1) ?? note.project;
  }

  protected noteProject(note: Note): ProjectSummary | null {
    if (note.projectId !== null) {
      return this.projects().find((project) => project.id === note.projectId) ?? null;
    }
    const proposal = this.selectedProposal();
    if (proposal?.reportId === note.id) {
      return this.projects().find((project) => project.id === proposal.projectId) ?? null;
    }
    return this.projects().find((project) =>
      project.primaryPath.toLocaleLowerCase() === note.project.toLocaleLowerCase()) ?? null;
  }

  protected openNoteProjectSettings(note: Note): void {
    const project = this.noteProject(note);
    if (!project) return;
    if (this.selectedProjectId() !== project.id) this.selectProject(project.id);
    this.projectSettingsOpen.set(true);
    this.previewProjectId.set(project.id);
    if (this.storageStatus()?.hasFirebaseConnection) this.loadRemoteProjects();
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

  protected updateProcessingComment(event: Event): void {
    this.processingComment.set((event.target as HTMLTextAreaElement).value);
  }

  protected updateRestoreComment(event: Event): void {
    this.restoreComment.set((event.target as HTMLTextAreaElement).value);
  }

  protected updateReprocessComment(event: Event): void {
    this.reprocessComment.set((event.target as HTMLTextAreaElement).value);
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
    if (!proposal || proposal.state !== 'processing' || !this.processingComment().trim() || this.actionPending()) {
      return;
    }

    const comment = this.processingComment().trim();
    this.actionPending.set(true);
    this.http
      .post<TaskProposal>(`/api/task-proposals/${proposal.id}/requeue`, { comment })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.selectedProposal.set(updated);
          this.processingComment.set('');
          this.notice.set('Заметка возвращена в очередь');
          this.actionPending.set(false);
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected retryProposal(comment: string): void {
    const proposal = this.selectedProposal();
    if (!proposal || !['failed', 'ready', 'unverified'].includes(proposal.state) || !comment.trim() || this.actionPending()) {
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
           return this.http.post<TaskProposal>(`/api/task-proposals/${proposal.id}/retry`, { comment: comment.trim() });
        }),
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.selectedProposal.set(updated);
           this.clarificationFormOpen.set(false);
           this.clarificationInstruction.set('');
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
    if (this.canRetryNote(note)) {
      this.retryProposal(instruction);
      return;
    }

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
    if (!note || !this.restoreComment().trim() || this.actionPending()) return;

    const comment = this.restoreComment().trim();
    this.actionPending.set(true);
    this.http
      .post<RestoreResult>(`/api/notes/${note.id}/restore`, { comment })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ workflow, proposal }) => {
          this.selectedWorkflow.set(workflow);
          this.selectedProposal.set(proposal);
          this.restoreFormOpen.set(false);
          this.restoreComment.set('');
          this.notice.set('Заметка возвращена в очередь на обработку');
          this.actionPending.set(false);
          this.refreshNotesReview();
        },
        error: (error: HttpErrorResponse) => this.finishActionWithError(error),
      });
  }

  protected reprocessCompletedNote(): void {
    const note = this.selectedNote();
    if (!note || this.reviewState(note) !== 'processed' || !this.reprocessComment().trim() || this.actionPending()) return;

    const comment = this.reprocessComment().trim();
    this.actionPending.set(true);
    this.http
      .post<ReprocessResult>(`/api/notes/${note.id}/reprocess`, { comment })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ workflow, proposal, deletedTaskId }) => {
          this.selectedWorkflow.set(workflow);
          this.selectedProposal.set(proposal);
          this.reprocessConfirmationOpen.set(false);
          this.reprocessComment.set('');
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
          if (this.selectedTaskStatus()) this.loadTasks();
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
        next: (projects) => {
          this.projects.set(projects);
          const path = new URLSearchParams(window.location.search).get('projectPath');
          if (path && !this.shortcutHandled) {
            this.shortcutHandled = true;
            const match = projects.find((project) => project.primaryPath.toLocaleLowerCase() === path.toLocaleLowerCase());
            if (match) this.selectProject(match.id);
            else this.notice.set('Репозиторий из ярлыка ещё не добавлен в проекты сайта. Добавьте его через +.');
          }
        },
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
          if (reset) this.workflows.set({});
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
    const version = ++this.workflowRequestVersion;
    const ids = this.notes().slice(-40).map((note) => note.id);
    if (ids.length === 0) {
      this.workflows.set({});
      return;
    }

    this.http
      .post<NoteWorkflowInfo[]>('/api/notes/workflows', { ids })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (workflows) => {
          if (version !== this.workflowRequestVersion) return;
          this.workflows.update((current) => ({
            ...current,
            ...Object.fromEntries(workflows.map((workflow) => [workflow.reportId, workflow])),
          }));
        },
        error: (error: HttpErrorResponse) => {
          if (version === this.workflowRequestVersion) this.setError(error);
        },
      });
  }

  private refreshNotesReview(): void {
    this.loadProjects();
    this.loadNotes(true);
    this.loadNoteCounts();
    this.loadProposal();
    this.loadSelectedReview();
  }

  private loadTasks(selectId?: string, reset = true): void {
    const version = ++this.taskRequestVersion;
    const cursor = reset ? null : this.taskNextBefore();
    if (!reset && cursor === null) return;
    reset ? this.taskLoading.set(true) : this.taskLoadingMore.set(true);
    let params = new HttpParams().set('limit', 40);
    if (this.selectedProjectId() !== null) params = params.set('projectId', this.selectedProjectId()!);
    if (this.selectedTaskStatus()) params = params.set('status', this.selectedTaskStatus());
    if (this.searchText()) params = params.set('query', this.searchText());
    if (cursor !== null) params = params.set('before', cursor);
    this.http
      .get<TaskPage>('/api/tasks', { params })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          if (version !== this.taskRequestVersion) return;
          const tasks = reset ? page.items : [...this.tasks(), ...page.items];
          this.tasks.set(tasks);
          this.taskTotal.set(page.total);
          this.taskNextBefore.set(page.nextBefore);
          const selectedId = selectId ?? this.selectedTask()?.id;
          const selected = tasks.find((task) => task.id === selectedId) ?? page.items[0] ?? null;
          this.selectedTask.set(selected);
          if (selected) this.loadTaskSources(selected);
          else this.taskSourceNames.set([]);
          this.taskLoading.set(false);
          this.taskLoadingMore.set(false);
        },
        error: (error: HttpErrorResponse) => {
          if (version !== this.taskRequestVersion) return;
          this.taskLoading.set(false);
          this.taskLoadingMore.set(false);
          this.setError(error);
        },
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
    this.error.set(error.error?.error ?? error.error?.title ?? 'Не удалось получить данные. Проверьте локальный сервер.');
  }

  private finishLoading(): void {
    this.loading.set(false);
    this.loadingMore.set(false);
  }
}
