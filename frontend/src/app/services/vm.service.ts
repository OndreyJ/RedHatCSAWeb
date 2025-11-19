import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, throwError } from 'rxjs';
import { environment } from '../environments/environment';

export interface ExamSession {
  sessionId: string;
  server1VmId: number;
  server2VmId: number;
  server3VmId: number;
  message: string;
}

export interface VmStatus {
  vmid: number;
  status: string;
  name: string;
  uptime: number;
}

export interface SessionStatus {
  sessionId: string;
  server1: VmStatus;
  server2: VmStatus;
  server3: VmStatus;
}

export interface Question {
  id: number;
  title: string;
  description: string;
  requiredVMs: string[];
  status: 'pending' | 'completed' | 'flagged';
  expanded: boolean;
}

export interface VncConsoleResponse {
  url: string;
  port: number;
  ticket: string;
  csrfToken?: string;
}

@Injectable({
  providedIn: 'root'
})
export class VmService {
  private apiUrl = environment.apiUrl;
  private currentSessionId: string | null = null;

  // Observables for real-time updates
  private sessionStatusSubject = new BehaviorSubject<SessionStatus | null>(null);
  public sessionStatus$ = this.sessionStatusSubject.asObservable();

  private questionsSubject = new BehaviorSubject<Question[]>(this.getDefaultQuestions());
  public questions$ = this.questionsSubject.asObservable();

  constructor(private http: HttpClient) {
    this.loadQuestionsFromStorage();
  }

  // Start a new exam session
  startExamSession(userId: string): Observable<ExamSession> {
    return new Observable(observer => {
      this.http.post<ExamSession>(`${this.apiUrl}/vm/session/start`, { userId })
        .subscribe({
          next: (session) => {
            this.currentSessionId = session.sessionId;
            localStorage.setItem('examSessionId', session.sessionId);
            this.startStatusPolling();
            observer.next(session);
            observer.complete();
          },
          error: (err) => observer.error(err)
        });
    });
  }

  // Get current session ID
  getSessionId(): string | null {
    if (!this.currentSessionId) {
      this.currentSessionId = localStorage.getItem('examSessionId');
    }
    return this.currentSessionId;
  }

  // Start a VM
  startVm(vmName: string): Observable<any> {
    const sessionId = this.getSessionId();
    if (!sessionId) {
      return throwError(() => new Error('No active session'));
    }
    return this.http.post(`${this.apiUrl}/vm/session/${sessionId}/vm/${vmName}/start`, {});
  }

  // Stop a VM
  stopVm(vmName: string): Observable<any> {
    const sessionId = this.getSessionId();
    if (!sessionId) {
      return throwError(() => new Error('No active session'));
    }
    return this.http.post(`${this.apiUrl}/vm/session/${sessionId}/vm/${vmName}/stop`, {});
  }

  // Get VM status
  getVmStatus(vmName: string): Observable<VmStatus> {
    const sessionId = this.getSessionId();
    if (!sessionId) {
      return throwError(() => new Error('No active session'));
    }
    return this.http.get<VmStatus>(`${this.apiUrl}/vm/session/${sessionId}/vm/${vmName}/status`);
  }

  // Get all VMs status
  getSessionStatus(): Observable<SessionStatus> {
    const sessionId = this.getSessionId();
    if (!sessionId) {
      return throwError(() => new Error('No active session'));
    }
    return this.http.get<SessionStatus>(`${this.apiUrl}/vm/session/${sessionId}/status`);
  }

  // Get noVNC console URL for VM (cookie-based authentication)
  getVncConsoleUrl(vmName: string): Observable<VncConsoleResponse> {
    const sessionId = this.getSessionId();
    if (!sessionId) {
      return throwError(() => new Error('No active session'));
    }
    return this.http.post<VncConsoleResponse>(
      `${this.apiUrl}/vm/session/${sessionId}/vm/${vmName}/console`,
      {},
      { withCredentials: true } // Important: include credentials for cookies
    );
  }

  // End exam session
  endSession(): Observable<any> {
    const sessionId = this.getSessionId();
    if (!sessionId) {
      return throwError(() => new Error('No active session'));
    }
    return new Observable(observer => {
      this.http.delete(`${this.apiUrl}/vm/session/${sessionId}`)
        .subscribe({
          next: (result) => {
            this.currentSessionId = null;
            localStorage.removeItem('examSessionId');
            this.stopStatusPolling();
            observer.next(result);
            observer.complete();
          },
          error: (err) => observer.error(err)
        });
    });
  }

  // Poll for status updates
  private statusPollingInterval: any;

  private startStatusPolling() {
    // Initial status fetch
    this.getSessionStatus().subscribe({
      next: (status) => this.sessionStatusSubject.next(status),
      error: (err) => console.error('Status fetch error:', err)
    });

    // Poll every 5 seconds
    this.statusPollingInterval = setInterval(() => {
      this.getSessionStatus().subscribe({
        next: (status) => this.sessionStatusSubject.next(status),
        error: (err) => console.error('Status polling error:', err)
      });
    }, 5000);
  }

  private stopStatusPolling() {
    if (this.statusPollingInterval) {
      clearInterval(this.statusPollingInterval);
      this.statusPollingInterval = null;
    }
    this.sessionStatusSubject.next(null);
  }

  // Question management
  getQuestions(): Question[] {
    return this.questionsSubject.value;
  }

  updateQuestionStatus(questionId: number, status: 'pending' | 'completed' | 'flagged') {
    const questions = this.questionsSubject.value.map(q =>
      q.id === questionId ? { ...q, status } : q
    );
    this.questionsSubject.next(questions);
    this.saveQuestionsToStorage(questions);
  }

  toggleQuestionExpanded(questionId: number) {
    const questions = this.questionsSubject.value.map(q =>
      q.id === questionId ? { ...q, expanded: !q.expanded } : q
    );
    this.questionsSubject.next(questions);
  }

  private saveQuestionsToStorage(questions: Question[]) {
    const sessionId = this.getSessionId();
    if (sessionId) {
      localStorage.setItem(`questions_${sessionId}`, JSON.stringify(questions));
    }
  }

  private loadQuestionsFromStorage() {
    const sessionId = this.getSessionId();
    if (sessionId) {
      const stored = localStorage.getItem(`questions_${sessionId}`);
      if (stored) {
        this.questionsSubject.next(JSON.parse(stored));
      }
    }
  }

  private getDefaultQuestions(): Question[] {
    return [
      {
        id: 1,
        title: "Configure Network Settings",
        description: "Configure the network interface with a static IP address 192.168.1.100/24, gateway 192.168.1.1, and DNS server 8.8.8.8. Use nmcli or NetworkManager to configure the connection.",
        requiredVMs: ["server1"],
        status: "pending",
        expanded: false
      },
      {
        id: 2,
        title: "Create User Accounts",
        description: "Create three user accounts: alice, bob, and charlie. Set alice as a member of the wheel group with sudo privileges. Set password requirements for all users.",
        requiredVMs: ["server1"],
        status: "pending",
        expanded: false
      },
      {
        id: 3,
        title: "Configure SELinux",
        description: "Ensure SELinux is running in enforcing mode. Configure the httpd service to have the correct SELinux context. Troubleshoot any SELinux denials.",
        requiredVMs: ["server1"],
        status: "pending",
        expanded: false
      },
      {
        id: 4,
        title: "Set Up NFS Share",
        description: "Configure server1 to share /shared directory via NFS. Mount this share on server2 at /mnt/nfs persistently. Ensure proper permissions.",
        requiredVMs: ["server1", "server2"],
        status: "pending",
        expanded: false
      },
      {
        id: 5,
        title: "Configure Firewall",
        description: "Use firewalld to allow HTTP, HTTPS, and SSH services. Block all other incoming traffic. Test your configuration.",
        requiredVMs: ["server1"],
        status: "pending",
        expanded: false
      },
      {
        id: 6,
        title: "Create Logical Volumes",
        description: "Create a new volume group named 'vg_data' with a 2GB logical volume named 'lv_data'. Format it with ext4 and mount it at /data persistently.",
        requiredVMs: ["server1"],
        status: "pending",
        expanded: false
      },
      {
        id: 7,
        title: "Configure Cron Jobs",
        description: "Create a cron job that runs every day at 2 AM to backup /etc to /backups/etc-backup-$(date +%Y%m%d).tar.gz",
        requiredVMs: ["server1"],
        status: "pending",
        expanded: false
      },
      {
        id: 8,
        title: "Set Up Apache Web Server",
        description: "Install and configure Apache to serve content from /var/www/html. Create a simple index.html page. Ensure it starts on boot.",
        requiredVMs: ["server1"],
        status: "pending",
        expanded: false
      }
    ];
  }

  resetExam() {
    this.questionsSubject.next(this.getDefaultQuestions());
    const sessionId = this.getSessionId();
    if (sessionId) {
      localStorage.removeItem(`questions_${sessionId}`);
    }
  }
}
