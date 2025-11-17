import { Component, OnInit, OnDestroy, ViewChild, ElementRef } from '@angular/core';
import { VmService, Question, SessionStatus } from '../services/vm.service';
import { Subscription } from 'rxjs';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

@Component({
  selector: 'app-exam-dashboard',
  imports: [CommonModule],
  templateUrl: './exam-dashboard.html',
  styleUrls: ['./exam-dashboard.css'], // fixed typo
  standalone: true,
})
export class ExamDashboard implements OnInit, OnDestroy {
  @ViewChild('terminalFrame', { static: false }) terminalFrame!: ElementRef;

  questions: Question[] = [];
  sessionStatus: SessionStatus | null = null;
  examStarted = false;
  loading = false;
  error: string | null = null;

  activeTerminal: string | null = null;
  terminalUrl: SafeResourceUrl | null = null;

  private subscriptions: Subscription[] = [];

  constructor(
    public vmService: VmService,
    private sanitizer: DomSanitizer
  ) { }

  ngOnInit(): void {
    const existingSession = this.vmService.getSessionId();
    if (existingSession) {
      this.examStarted = true;
      this.loadSessionData();
    }

    this.subscriptions.push(
      this.vmService.questions$.subscribe(qs => (this.questions = qs))
    );

    this.subscriptions.push(
      this.vmService.sessionStatus$.subscribe(status => (this.sessionStatus = status))
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(sub => sub.unsubscribe());
  }

  // --- Exam session ---
  startExam(): void {
    this.loading = true;
    this.error = null;

    this.vmService.startExamSession('user-' + Date.now()).subscribe({
      next: (session) => {
        this.examStarted = true;
        this.loading = false;
      },
      error: (err) => {
        console.error('Failed to start exam:', err);
        this.error = 'Failed to start exam session. Please try again.';
        this.loading = false;
      }
    });
  }

  endExam(): void {
    if (!confirm('Are you sure you want to end the exam? All VMs will be deleted.')) return;

    this.loading = true;
    this.vmService.endSession().subscribe({
      next: () => {
        this.examStarted = false;
        this.sessionStatus = null;
        this.activeTerminal = null;
        this.terminalUrl = null;
        this.loading = false;
        alert('Exam session ended successfully.');
      },
      error: (err) => {
        console.error('Failed to end exam:', err);
        this.error = 'Failed to end exam session.';
        this.loading = false;
      }
    });
  }

  private loadSessionData(): void {
    this.vmService.getSessionStatus().subscribe({
      next: (status) => this.sessionStatus = status,
      error: (err) => console.error('Failed to load session:', err)
    });
  }

  toggleQuestion(questionId: number): void {
    this.vmService.toggleQuestionExpanded(questionId);
  }

  updateQuestionStatus(questionId: number, status: 'pending' | 'completed' | 'flagged'): void {
    this.vmService.updateQuestionStatus(questionId, status);
  }

  resetExam(): void {
    if (!confirm('Reset all question progress?')) return;
    this.vmService.resetExam();
  }

  // --- VM control ---
  startVm(vmName: string): void {
    this.loading = true;
    this.vmService.startVm(vmName).subscribe({
      next: () => {
        this.loading = false;
        setTimeout(() => this.loadSessionData(), 2000);
      },
      error: (err) => {
        console.error(`Failed to start ${vmName}:`, err);
        this.error = `Failed to start ${vmName}`;
        this.loading = false;
      }
    });
  }

  stopVm(vmName: string): void {
    this.loading = true;
    this.vmService.stopVm(vmName).subscribe({
      next: () => {
        if (this.activeTerminal === vmName) this.closeTerminal();
        this.loading = false;
        setTimeout(() => this.loadSessionData(), 2000);
      },
      error: (err) => {
        console.error(`Failed to stop ${vmName}:`, err);
        this.error = `Failed to stop ${vmName}`;
        this.loading = false;
      }
    });
  }

  getVmStatus(vmName: string): string {
    if (!this.sessionStatus) return 'unknown';
    switch (vmName) {
      case 'server1': return this.sessionStatus.server1?.status || 'unknown';
      case 'server2': return this.sessionStatus.server2?.status || 'unknown';
      case 'server3': return this.sessionStatus.server3?.status || 'unknown';
      default: return 'unknown';
    }
  }

  getVmId(vmName: string): number | null {
    if (!this.sessionStatus) return null;
    switch (vmName) {
      case 'server1': return this.sessionStatus.server1?.vmid || null;
      case 'server2': return this.sessionStatus.server2?.vmid || null;
      case 'server3': return this.sessionStatus.server3?.vmid || null;
      default: return null;
    }
  }

  isVmRunning(vmName: string): boolean {
    return this.getVmStatus(vmName) === 'running';
  }

  // --- Terminal (iframe method) ---
  openTerminal(vmName: string): void {
    if (!this.isVmRunning(vmName)) {
      alert('Please start the VM first.');
      return;
    }

    const vmId = this.getVmId(vmName);
    if (!vmId) {
      alert('VM ID not found.');
      return;
    }

    this.loading = true;
    this.error = null;

    this.vmService.getVncConsoleUrl(vmName).subscribe({
      next: (response) => {
        this.terminalUrl = this.sanitizer.bypassSecurityTrustResourceUrl(response.url);
        this.activeTerminal = vmName;
        this.loading = false;
      },
      error: (err) => {
        console.error('Failed to get console URL:', err);
        this.error = 'Failed to open terminal console.';
        this.loading = false;
      }
    });
  }

  closeTerminal(): void {
    this.activeTerminal = null;
    this.terminalUrl = null;
  }

  // --- Helpers ---
  getStatusIconClass(status: string): string {
    switch (status) {
      case 'completed': return 'status-completed';
      case 'flagged': return 'status-flagged';
      default: return 'status-pending';
    }
  }

  getProgressPercentage(): number {
    if (this.questions.length === 0) return 0;
    const completed = this.questions.filter(q => q.status === 'completed').length;
    return Math.round((completed / this.questions.length) * 100);
  }
}
