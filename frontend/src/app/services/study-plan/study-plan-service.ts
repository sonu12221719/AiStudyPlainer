import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface StudyPlanRequest {
  examName: string;
  startDate: string;
  endDate: string;
  dailyStudyHours: number;
  focusSubjects: string[];
}

export interface StudyPlan {
  id: string;
  examName: string;
  startDate: string;
  endDate: string;
  dailyStudyHours: number;
  focusSubjects: string[];
  createdAt: string;
}

@Injectable({
  providedIn: 'root',
})
export class StudyPlanService {
  private baseUrl = 'http://localhost:5208/api/';
  constructor(private http: HttpClient) {}

  create(payload: StudyPlanRequest): Observable<StudyPlan> {
    return this.http.post<StudyPlan>(`${this.baseUrl}/Planner/preset`, payload);
  }
}
