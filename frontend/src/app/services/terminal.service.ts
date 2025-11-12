import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Terminal } from 'xterm';
import { FitAddon } from 'xterm-addon-fit';
import { environment } from '../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class TerminalService {
  private connection: signalR.HubConnection | null = null;
  private terminal: Terminal | null = null;
  private fitAddon: FitAddon | null = null;

  constructor() { }

  // Create and initialize terminal
  createTerminal(container: HTMLElement): Terminal {
    this.terminal = new Terminal({
      cursorBlink: true,
      fontSize: 14,
      fontFamily: 'Menlo, Monaco, "Courier New", monospace',
      theme: {
        background: '#1e1e1e',
        foreground: '#d4d4d4',
        cursor: '#d4d4d4',
        black: '#000000',
        red: '#cd3131',
        green: '#0dbc79',
        yellow: '#e5e510',
        blue: '#2472c8',
        magenta: '#bc3fbc',
        cyan: '#11a8cd',
        white: '#e5e5e5',
        brightBlack: '#666666',
        brightRed: '#f14c4c',
        brightGreen: '#23d18b',
        brightYellow: '#f5f543',
        brightBlue: '#3b8eea',
        brightMagenta: '#d670d6',
        brightCyan: '#29b8db',
        brightWhite: '#e5e5e5'
      }
    });

    this.fitAddon = new FitAddon();
    this.terminal.loadAddon(this.fitAddon);
    this.terminal.open(container);
    this.fitAddon.fit();

    // Handle terminal input
    this.terminal.onData((data) => {
      if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
        this.connection.invoke('SendInput', data).catch(err => console.error(err));
      }
    });

    // Handle window resize
    window.addEventListener('resize', () => {
      if (this.fitAddon) {
        this.fitAddon.fit();
      }
    });

    return this.terminal;
  }

  // Connect to VM via SignalR
  async connectToVm(sessionId: string, vmIp: string, username: string, password: string): Promise<void> {
    if (this.connection) {
      await this.disconnect();
    }

    // Create SignalR connection
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/terminal`)
      .withAutomaticReconnect()
      .build();

    // Handle incoming output
    this.connection.on('Output', (data: string) => {
      if (this.terminal) {
        this.terminal.write(data);
      }
    });

    // Handle connection status
    this.connection.on('Connected', (message: string) => {
      if (this.terminal) {
        this.terminal.writeln(`\r\n${message}\r\n`);
      }
    });

    // Handle errors
    this.connection.on('Error', (error: string) => {
      if (this.terminal) {
        this.terminal.writeln(`\r\n\x1b[31mError: ${error}\x1b[0m\r\n`);
      }
    });

    try {
      await this.connection.start();
      console.log('SignalR connected');

      // Connect to the VM
      await this.connection.invoke('ConnectToVm', sessionId, vmIp, username, password);
    } catch (err) {
      console.error('SignalR connection error:', err);
      throw err;
    }
  }

  // Disconnect terminal
  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }

    if (this.terminal) {
      this.terminal.dispose();
      this.terminal = null;
    }
  }

  // Send command to terminal
  sendCommand(command: string): void {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      this.connection.invoke('SendCommand', command).catch(err => console.error(err));
    }
  }

  // Clear terminal
  clear(): void {
    if (this.terminal) {
      this.terminal.clear();
    }
  }

  // Fit terminal to container
  fit(): void {
    if (this.fitAddon) {
      this.fitAddon.fit();
    }
  }

  // Get terminal instance
  getTerminal(): Terminal | null {
    return this.terminal;
  }

  // Check if connected
  isConnected(): boolean {
    return this.connection !== null &&
      this.connection.state === signalR.HubConnectionState.Connected;
  }
}
