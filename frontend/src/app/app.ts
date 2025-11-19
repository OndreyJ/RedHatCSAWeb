import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ExamDashboard } from './exam-dashboard/exam-dashboard';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ExamDashboard],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('frontend');
}
