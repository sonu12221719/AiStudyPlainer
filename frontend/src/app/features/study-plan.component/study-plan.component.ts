import { Component, HostBinding, inject, signal } from '@angular/core';
import { StudyPlanService } from '../../services/study-plan/study-plan-service';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-study-plan.component',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './study-plan.component.html',
  styleUrl: './study-plan.component.css',
})
export class StudyPlanComponent {
  @HostBinding('class') class = 'flex-1 flex flex-col overflow-auto';

  private studyPlanService = inject(StudyPlanService);

  
  /** To generate study plan from gemini */
  usePresetDisplay = signal(false);
  openPreset(){
    this.usePresetDisplay.set(true);
  }
  closePreset(){
    this.usePresetDisplay.set(false);
  }

  examName = '';
  startDate = '';
  endDate = '';
  dailyStudyHours = 1;
  focusSubjects = '';
  
  async generatePlan(): Promise<void> {
    const subjectSplits = this.focusSubjects.split(',').map(s => s.trim()).filter(s => s.length > 0);
    const payload = {
      examName: this.examName,
      startDate: this.startDate,
      endDate: this.endDate,
      dailyStudyHours: Number(this.dailyStudyHours),
      focusSubjects: subjectSplits
    };
    try {
      const studyPlan = await this.studyPlanService.create(payload).toPromise();
      console.log('Generated Study Plan:', studyPlan);
    } catch (error) {
      console.error('Error generating study plan:', error);
    }
  }

  /** Upload Syllabus to get time tables */
  useUploadDisplay = signal(false);
  
  openUploadMaterial(){
    this.useUploadDisplay.set(true);
  }
  closeUploadMaterial(){
    this.useUploadDisplay.set(false);
  }
  async uploadPlan(): Promise<void> {

  }
}
