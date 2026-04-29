import { Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import axios from 'axios';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private apiUrl = 'http://localhost:5208/api/Auth';
  private loading = signal(false);

  isLoading = this.loading.asReadonly();
  
  constructor(private router: Router) {}

  async login(email: string, password: string): Promise<void> {
    this.loading.set(true);
    const payload = {
      Email: email.trim(),
      Password: password.trim()
    };

    try {
      const response = await axios.post(`${this.apiUrl}/login`, payload);
      
      if (!response || response.status !== 200) {
        throw new Error('Login failed');
      }
      sessionStorage.setItem('token', response.data.token);
      this.router.navigate(['/dashboard']);
    } catch (error) {
      console.error(error);
      alert("Login failed. Please check your credentials and try again.");
    } finally {
      this.loading.set(false);
    }
  }
}
