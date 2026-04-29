import axios from 'axios';
import { Component, inject, signal, Signal } from '@angular/core';
import { CommonModule} from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../services/auth-service';
@Component({
  selector: 'app-login',
  imports: [
    CommonModule, FormsModule
  ],
  templateUrl: './login.html'
})
export class Login {
  private authService = inject(AuthService);

  email = '';
  password = '';
  errorMessage = '';
  loading = this.authService.isLoading;

  async onsubmit(): Promise<void> {
    this.errorMessage = '';
    try {
      await this.authService.login(this.email, this.password);
      this.email = '';
      this.password = '';
    } catch (error: any) {
      this.errorMessage = 'Login failed. Please check your credentials and try again.';
    }
  }
  
}
