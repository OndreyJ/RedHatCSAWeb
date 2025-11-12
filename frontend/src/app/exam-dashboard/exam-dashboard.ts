import { Component, OnInit, OnDestroy, ViewChild, ElementRef } from '@angular/core';
import { VmService, Question, SessionStatus } from '../services/vm.service';
import { TerminalService } from '../services/terminal.service';
import { Subscription } from 'rxjs';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-exam-dashboard',
  imports: [CommonModule],
  templateUrl: './exam-dashboard.html',
  styleUrl: './exam-dashboard.css',
  standalone: true,
})

export class ExamDashboard implements OnInit, OnDestroy {
  @ViewChild('terminalContainer', { static: false }) terminalContainer!: ElementRef;

  questions: Question[] = [];
  sessionStatus: SessionStatus | null = null;
  examStarted = false;
  loading = false;
  error: string | null = null;

  activeTerminal: string | null = null;
  terminalConnected = false;

  private subscriptions: Subscription[] = [];

  // VM credentials (in production, get from secure storage)
  private vmCredentials = {
    username: 'root',
    password: 'your-password-here'
  };

  constructor(
    public vmService: VmService,
    private terminalService: TerminalService
  ) { }

  ngOnInit(): void {
    // Check if there's an existing session
    const existingSession = this.vmService.getSessionId();
    if (existingSession) {
      this.examStarted = true;
      this.loadSessionData();
    }

    // Subscribe to questions
    this.subscriptions.push(
      this.vmService.questions$.subscribe(questions => {
        this.questions = questions;
      })
    );

    // Subscribe to session status
    this.subscriptions.push(
      this.vmService.sessionStatus$.subscribe(status => {
        this.sessionStatus = status;
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(sub => sub.unsubscribe());
    if (this.terminalConnected) {
      this.terminalService.disconnect();
    }
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
      next: (status) => {
        this.sessionStatus = status;
      },
      error: (err) => {
        console.error('Failed to load session:', err);
      }
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
    this.vmService.startVm(vmName).subscribe({
      next: () => {
        console.log(`${vmName} started`);
        this.loading = false;
        // Refresh status
        setTimeout(() => this.loadSessionData(), 2000);
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
    this.vmService.stopVm(vmName).subscribe({
      next: () => {
        console.log(`${vmName} stopped`);
        this.loading = false;
        // Close terminal if this VM's terminal is open
        if (this.activeTerminal === vmName) {
          this.disconnectTerminal();
        }
        // Refresh status
        setTimeout(() => this.loadSessionData(), 2000);
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

  // Open terminal for VM
  async openTerminal(vmName: string): Promise<void> {
    if (!this.isVmRunning(vmName)) {
      alert('Please start the VM first.');
      return;
    }

    // Only server1 and server2 have terminal access
    if (vmName !== 'server1' && vmName !== 'server2') {
      alert('Terminal access not available for this VM.');
      return;
    }

    try {
      // Create terminal in the container
      this.activeTerminal = vmName;

      // Wait for view to update
      setTimeout(async () => {
        if (this.terminalContainer) {
          this.terminalService.createTerminal(this.terminalContainer.nativeElement);

          // Get VM IP address (you need to implement this based on your network setup)
          const vmIp = this.getVmIpAddress(vmName);

          // Connect to VM
          await this.terminalService.connectToVm(
            this.vmService.getSessionId()!,
            vmIp,
            this.vmCredentials.username,
            this.vmCredentials.password
          );

          this.terminalConnected = true;
        }
      }, 100);
    } catch (err) {
      console.error('Failed to open terminal:', err);
      this.error = 'Failed to connect to terminal';
      this.activeTerminal = null;
    }
  }

  // Close terminal
  disconnectTerminal(): void {
    this.terminalService.disconnect();
    this.activeTerminal = null;
    this.terminalConnected = false;
  }

  // Get VM IP address (implement based on your network setup)
  private getVmIpAddress(vmName: string): string {
    // In production, you should get this from the API
    // For now, return placeholder IPs
    switch (vmName) {
      case 'server1': return '192.168.1.101';
      case 'server2': return '192.168.1.102';
      default: return '192.168.1.100';
    }
  }

  // Get status icon class
  getStatusIconClass(status: string): string {
    switch (status) {
      case 'completed': return 'status-completed';
      case 'flagged': return 'status-flagged';
      default: return 'status-pending';
    }
  }

  // Get progress percentage
  getProgressPercentage(): number {
    if (this.questions.length === 0) return 0;
    const completed = this.questions.filter(q => q.status === 'completed').length;
    return Math.round((completed / this.questions.length) * 100);
  }

  // Reset exam (for testing)
  resetExam(): void {
    if (!confirm('Reset all question progress?')) {
      return;
    }
    this.vmService.resetExam();
  }
}
