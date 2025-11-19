import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, throwError } from 'rxjs';
import { environment } from '../environments/environment';

export interface ExamSession {
  sessionId: string;
  server1VmId: number;
  server2VmId: number;
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

export interface basicConsoleResponse {
  url: string;
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

  // get basic url that will open a console in a new tab
  getBasicUrl(vmName: string): Observable<basicConsoleResponse> {
    const sessionId = this.getSessionId();
    if (!sessionId) {
      return throwError(() => new Error('No active session'));
    }
    return this.http.post<basicConsoleResponse>(
      `${this.apiUrl}/vm/session/${sessionId}/vm/${vmName}/url`,
      {},
    );
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
      title: "Break into server2 and set the password to root",
      description: "Set the password for root on server2 to 'root'. Set the target to multi-user and ensure it boots into that automatically. Reboot to confirm.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 2,
      title: "Configure network interfaces and hostnames on node 1 and 2",
      description: "Configure the network interfaces and hostnames on node1 and node2 with the following details:\n- Subnet: /24\n- Gateway: 192.168.9.1\n- node1 IP: 192.168.9.11, node2 IP: 192.168.9.12\n- DNS: 8.8.8.8",
      requiredVMs: ["node1", "node2"],
      status: "pending",
      expanded: false
    },
    {
      id: 3,
      title: "Ensure network services start at boot",
      description: "Ensure that network services start at boot on both node1 and node2.",
      requiredVMs: ["node1", "node2"],
      status: "pending",
      expanded: false
    },
    {
      id: 4,
      title: "Enable SSH access for root on both servers",
      description: "Enable root login via SSH on both server1 and server2.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 5,
      title: "Enable key-based SSH authentication for root on both servers",
      description: "Enable key-based SSH authentication for root on both server1 and server2.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 6,
      title: "Configure the repositories on server1",
      description: "Configure the repositories on server1 to point to the following URLs:\n- http://192.168.9.10/BaseOS/\n- http://192.168.9.10/AppStream/",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 7,
      title: "Secure copy the repo file to server2",
      description: "Secure copy the repository configuration file from server1 to server2.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 8,
      title: "Configure autofs for home directories",
      description: "Configure autofs to automatically mount individual users' home directories from `/export/home` on 192.168.9.10 to `/mnt/autofs_home/<user_name>` on both servers. Test with user 'cindy'.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 9,
      title: "Configure file creation permissions",
      description: "Configure both servers to create files with 660 permissions by default for all users.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 10,
      title: "Set password policies",
      description: "Set the password policy to require a minimum of 6 characters and a maximum age of 60 days for all users.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 11,
      title: "Create users and groups based on file",
      description: "Create the following users and groups based on the contents of the provided file. Use bash scripts and ensure all users except 'cindy' use autofs for their home directories:\n```\nmanny:1010:dba_admin,dba_managers,dba_staff\nmoe:1011:dba_admin,dba_staff\njack:1012:dba_intern,dba_staff\nmarcia:1013:it_staff,it_managers\njan:1014:dba_admin,dba_staff\ncindy:1015:dba_managers,dba_staff\n```",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 12,
      title: "Set user passwords",
      description: "Set the password for the new users to 'SecurePhrase!'",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 13,
      title: "Create sudo alias for MESSAGE command on node1",
      description: "Create a sudo command alias on node1 for 'MESSAGE' with the command `/bin/tail -f /var/log/messages`.",
      requiredVMs: ["node1"],
      status: "pending",
      expanded: false
    },
    {
      id: 14,
      title: "Enable superuser privileges for users",
      description: "Enable the following superuser privileges:\n- dba_managers: everything\n- dba_admin: SOFTWARE, SERVICES, PROCESSES\n- dba_intern: MESSAGES",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 15,
      title: "Create a gzip archive of /etc on server1",
      description: "Create a gzip archive of the /etc directory called `etc_archive.gz` in the /archives directory on server1.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 16,
      title: "Create a bzip2 archive of /usr/share/doc on server1",
      description: "Create a bzip2 archive of the /usr/share/doc directory called `doc_archive.bz2` in the /archives directory on server1.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 17,
      title: "Create a gzip archive of /etc on server1",
      description: "Create a gzip archive of the /etc directory called 'etc_archive.gz' in the /archives directory on server1.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 18,
      title: "Create a bzip2 archive of /usr/share/doc on server1",
      description: "Create a bzip2 archive of the /usr/share/doc directory called 'doc_archive.bz2' in the /archives directory on server1.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 19,
      title: "Create symbolic and hard links",
      description: "On server1, create a folder called '/links', and under it create a file called 'file1'. Then, create a symbolic link called 'file2' pointing to 'file1' and a hard link called 'file3' pointing to 'file1'. Verify your work.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 20,
      title: "Find all setuid files on server1",
      description: "Find all setuid files on server1 and save the list to /root/suid.txt.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 21,
      title: "Find all files larger than 3MB in /etc",
      description: "Find all files larger than 3MB in the /etc directory on server1 and copy them to /largefiles.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 22,
      title: "Mount /export/dba_files persistently on server1",
      description: "On server1, persistently mount '/export/dba_files' from server 192.168.9.10 under '/mnt/dba_files'. Ensure that manny is the user owner and dba_staff is the group owner. Ensure the groupID is applied to newly created files. Ensure users can only delete files they have created.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 23,
      title: "Mount /export/it_files persistently on server1",
      description: "On server1, persistently mount '/export/it_files' from server 192.168.9.10 under '/mnt/it_files'. Ensure that marcia is the user owner and it_staff is the group owner. Ensure the groupID is applied to newly created files. Ensure users can only delete files they have created.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 24,
      title: "Create an 'at' job to write to a file",
      description: "Create a job using the 'at' command to write 'This task was easy!' to /coolfiles/at_job.txt in 10 minutes.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 25,
      title: "Create a 'cron' job to log a message",
      description: "Create a job using 'cron' to write 'Wow! I'm going to pass this test!' every Tuesday at 3pm to /var/log/messages.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 26,
      title: "Write a script 'awesome.sh' on server1",
      description: "Write a script named awesome.sh in the root directory on server1. The script should behave as follows:\n- If 'me' is given as an argument, it should output 'Yes, I’m awesome.'\n- If 'them' is given as an argument, it should output 'Okay, they are awesome.'\n- If no argument or anything else is given, it should output 'Usage ./awesome.sh me|them'",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 27,
      title: "Fix web server on server1",
      description: "Fix the web server on server1 and make sure all files are accessible. Do not make any changes to the web server configuration files. Ensure it's accessible from server2 and the client browser.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 28,
      title: "Put SELinux on permissive mode on server2",
      description: "Put SELinux on server2 in permissive mode.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 29,
      title: "Modify bootloader on server1",
      description: "On server1, modify the bootloader with the following parameters:\n- Increase the timeout using GRUB_TIMEOUT=1\n- Add the following line: GRUB_TIMEOUT_STYLE=hidden\n- Add quiet to the end of the GRUB_CMDLINE_LINUX line",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 30,
      title: "Configure NTP synchronization",
      description: "Configure NTP synchronization on both servers. Point them to us.pool.ntp.org.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 31,
      title: "Configure persistent journaling on both servers",
      description: "Configure persistent journaling on both servers.",
      requiredVMs: ["server1", "server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 32,
      title: "Create volume group on server2",
      description: "On server2, create a new 2GiB volume group on /dev/sdb named 'platforms_vg'.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 33,
      title: "Create logical volume on server2",
      description: "Under the 'platforms_vg' volume group on server2, create a 500MiB logical volume named 'platforms_lv' and format it as ext4.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 34,
      title: "Mount logical volume on server2",
      description: "Mount the 'platforms_lv' logical volume persistently under /mnt/platforms_lv on server2. Ensure the 'dba_interns' group cannot rwx access to it.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 35,
      title: "Extend the volume on server2",
      description: "Extend the 'platforms_lv' volume and partition by 500MiB on server2.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 36,
      title: "Create a swap partition on server2",
      description: "On server2, create a 500MiB swap partition on /dev/sdb and mount it persistently.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 37,
      title: "Create networks volume group on server2",
      description: "On server2, using the remaining space on /dev/sdb, create a volume group with the name 'networks_vg'.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 38,
      title: "Create logical volume 'networks_lv' on server2",
      description: "Under the 'networks_vg' volume group on server2, create a logical volume named 'networks_lv'. It should use 8 MiB extents and 75 extents in total. Format it with the vfat filesystem and ensure it mounts persistently on /mnt/networks_lv.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 39,
      title: "Create thin-provisioned volume on server2",
      description: "On server2, create a 5TB thin-provisioned volume on /dev/sdc called 'thin_vol', backed by a pool called 'thin_pool' on a 5GB volume group named 'thin_vg'. Format it as xfs and mount it persistently under /mnt/thin_vol. Ensure Jack does not have any permissions.",
      requiredVMs: ["server2"],
      status: "pending",
      expanded: false
    },
    {
      id: 40,
      title: "Set merged tuned profile on server1",
      description: "On server1, set a merged tuned profile using the powersave and latency profiles.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 41,
      title: "Stress-ng Process Management on server1",
      description: "On server1, start one stress-ng process with the niceness value of 19. Adjust the niceness value of the stress process to 10. Finally, kill the stress process.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 42,
      title: "Create a container image from a Containerfile",
      description: "On server1, as the user 'cindy', create a container image from http://192.168.9.10/containers/Containerfile with the tag 'web_image'.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 43,
      title: "Deploy container as a service with 'cindy_web'",
      description: "From the newly created image, deploy a container as a service with the container name 'cindy_web'. The web config files should map to ~/web_files, and the local port of 8000 should be mapped to the container's port 80. Create a default page that says 'Welcome to Cindy's Web Server!'. Ensure the service is enabled and the website is accessible.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 44,
      title: "Add a flatpak repo using the URL method",
      description: "On server1, add a flatpak repository using the URL method from 'https://dl.flathub.org/repo/flathub.flatpakrepo'. Test by installing something from the repository.",
      requiredVMs: ["server1"],
      status: "pending",
      expanded: false
    },
    {
      id: 45,
      title: "Remove the flatpak repo and use file method",
      description: "On server1, remove the flatpak repository added in task 44. Then, add the repository again using the file method with the following configuration:\n\n[Flatpak Repo]\nTitle=Flathub\nUrl=https://dl.flathub.org/repo/\nGPGKey=mQIN... (Full GPG Key)...\nTest by installing something from this repository.",
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
