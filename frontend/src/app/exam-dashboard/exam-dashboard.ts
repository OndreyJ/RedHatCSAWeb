import { Component, OnInit, OnDestroy, ViewChild, ElementRef } from '@angular/core';
import { VmService, Question, SessionStatus } from '../services/vm.service';
import { Subscription } from 'rxjs';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

@Component({
  selector: 'app-exam-dashboard',
  imports: [CommonModule],
  templateUrl: './exam-dashboard.html',
  styleUrls: ['./exam-dashboard.css'],
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
    // Load existing session if any
    const existingSession = this.vmService.getSessionId();
    if (existingSession) {
      this.examStarted = true;
      this.loadSessionData();
    }

    // Subscribe to questions
    this.subscriptions.push(
      this.vmService.questions$.subscribe(questions => this.questions = questions)
    );

    // Subscribe to session status
    this.subscriptions.push(
      this.vmService.sessionStatus$.subscribe(status => this.sessionStatus = status)
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(sub => sub.unsubscribe());
  }

  // Start exam session
  startExam(): void {
    this.loading = true;
    this.error = null;

    this.vmService.startExamSession('user-' + Date.now()).subscribe({
      next: (session) => {
        console.log('Exam session started:', session);
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

  // End exam session
  endExam(): void {
    if (!confirm('Are you sure you want to end the exam? All VMs will be deleted.')) {
      return;
    }

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

  // Load session data
  private loadSessionData(): void {
    this.vmService.getSessionStatus().subscribe({
      next: (status) => this.sessionStatus = status,
      error: (err) => console.error('Failed to load session:', err)
    });
  }

  // Toggle question expansion
  toggleQuestion(questionId: number): void {
    this.vmService.toggleQuestionExpanded(questionId);
  }

  // Update question status
  updateQuestionStatus(questionId: number, status: 'pending' | 'completed' | 'flagged'): void {
    this.vmService.updateQuestionStatus(questionId, status);
  }

  // Start VM
  startVm(vmName: string): void {
    this.loading = true;
    this.error = null;

    this.vmService.startVm(vmName).subscribe({
      next: () => {
        console.log(`${vmName} started`);

        // Fetch updated sessionStatus immediately
        this.vmService.getSessionStatus().subscribe({
          next: (status) => {
            this.sessionStatus = status;

            // Optionally open terminal if user clicked right after start
            if (this.activeTerminal === vmName) {
              this.openTerminal(vmName);
            }

            this.loading = false;
          },
          error: (err) => {
            console.error('Failed to refresh session status:', err);
            this.loading = false;
          }
        });
      },
      error: (err) => {
        console.error(`Failed to start ${vmName}:`, err);
        this.error = `Failed to start ${vmName}`;
        this.loading = false;
      }
    });
  }

  // Stop VM
  stopVm(vmName: string): void {
    this.loading = true;
    this.error = null;

    this.vmService.stopVm(vmName).subscribe({
      next: () => {
        console.log(`${vmName} stopped`);
        if (this.activeTerminal === vmName) {
          this.closeTerminal();
        }
        // Refresh session status
        this.vmService.getSessionStatus().subscribe({
          next: (status) => this.sessionStatus = status,
          error: (err) => console.error('Failed to refresh status:', err)
        });
        this.loading = false;
      },
      error: (err) => {
        console.error(`Failed to stop ${vmName}:`, err);
        this.error = `Failed to stop ${vmName}`;
        this.loading = false;
      }
    });
  }

  // Get VM status
  getVmStatus(vmName: string): string {
    if (!this.sessionStatus) return 'unknown';
    switch (vmName) {
      case 'server1': return this.sessionStatus.server1?.status || 'unknown';
      case 'server2': return this.sessionStatus.server2?.status || 'unknown';
      case 'server3': return this.sessionStatus.server3?.status || 'unknown';
      default: return 'unknown';
    }
  }

  // Check if VM is running
  isVmRunning(vmName: string): boolean {
    return this.getVmStatus(vmName) === 'running';
  }

  openTerminal(vmName: string): void {
    if (!this.isVmRunning(vmName)) {
      alert("Please start the VM first.");
      return;
    }

    this.loading = true;
    this.error = null;

    this.vmService.getBasicUrl(vmName).subscribe({
      next: (response) => {
        if (!response?.url) {
          this.error = "No console URL returned from backend.";
          this.loading = false;
          return;
        }

        window.open(response.url, '_blank');

        this.loading = false;
      },
      error: (err) => {
        this.error = `Failed to open terminal: ${err.error?.message || err.message}`;
        this.loading = false;
      }
    });
  }


  // Open Proxmox noVNC terminal
  // openTerminal(vmName: string): void {
  //   console.log("=== openTerminal called ===");
  //   console.log("VM Name:", vmName);
  //   console.log("Session ID:", this.vmService.getSessionId());
  //   console.log("Session Status:", this.sessionStatus);
  //
  //   if (!this.isVmRunning(vmName)) {
  //     alert("Please start the VM first.");
  //     return;
  //   }
  //
  //   this.loading = true;
  //   this.error = null;
  //
  //   console.log("Calling backend console endpoint...");
  //
  //   // Call backend to get noVNC URL - backend handles VM ID lookup
  //   this.vmService.getVncConsoleUrl(vmName).subscribe({
  //     next: (response) => {
  //       console.log("Backend response:", response);
  //
  //       if (!response?.url) {
  //         this.error = "No console URL returned from backend.";
  //         this.loading = false;
  //         console.error("Empty URL response:", response);
  //         return;
  //       }
  //
  //       this.terminalUrl = this.sanitizer.bypassSecurityTrustResourceUrl(response.url);
  //       this.activeTerminal = vmName;
  //       this.loading = false;
  //
  //       console.log("Console opened successfully!");
  //     },
  //     error: (err) => {
  //       console.error("Failed to get console URL:", err);
  //       console.error("Error status:", err.status);
  //       console.error("Error body:", err.error);
  //       this.error = `Failed to open terminal: ${err.error?.message || err.message || 'Unknown error'}`;
  //       this.loading = false;
  //     }
  //   });
  // }

  // Close terminal
  closeTerminal(): void {
    this.activeTerminal = null;
    this.terminalUrl = null;
  }

  // Question helpers
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

  resetExam(): void {
    if (!confirm('Reset all question progress?')) return;
    this.vmService.resetExam();
  }
}
