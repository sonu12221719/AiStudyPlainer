import { Component, signal, Signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import axios from 'axios';

@Component({
  selector: 'app-signup',
  imports: [FormsModule],
  templateUrl: './signup.html',
  styleUrl: './signup.css',
})
export class Signup {
  fullName = '';
  email = ''
  password = '';
  confirmPassword = '';
  exam = '';
  examDate = '';
  dailyStudyHours = '';
  loading = signal(false);
  uri:string="http://localhost:5208/api/Auth/register";
  async onsubmit(): Promise<void> {
    this.loading.set(true);
    if(this.password.trim() !== this.confirmPassword.trim()){
      alert("Passwords do not match!");
      this.loading.set(false);
      return;
    }
    const payload = {
      FullName: this.fullName.trim(),
      Email: this.email.trim(),
      Password: this.password.trim(),
      ExamTarget: this.exam.trim(),
      ExamDate: this.examDate.trim(),
      DailyStudyHours: Number(this.dailyStudyHours)
    };

    try {
      const response = await axios.post(this.uri, payload);
      if(response.status === 201){
        alert("Registration successful! Please log in.");
        this.fullName = '';
        this.email = '';
        this.password = '';
        this.confirmPassword = '';
        this.exam = '';
        this.examDate = '';
        this.dailyStudyHours = '';
      } else {
        alert("Registration failed. Please try again.");
        this.loading.set(false);
        return;
      }
      this.loading.set(false);
    } catch (error) {
      console.error(error);
      alert("An error occurred during registration. Please try again.");
      this.loading.set(false);
    }
    
  }
}
