import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

export const authGuard: CanActivateFn = (route, state) => {
  const router = inject(Router);

  const token = sessionStorage.getItem('token');
  if (!token) {
    alert("You are not authorized to access this page. Please log in.");
    router.navigate(['/login']);
    return false;
  }

  if(isTokenExpired(token)){
    alert("Your session has expired. Please log in again.");
    sessionStorage.removeItem('token');
    router.navigate(['/login']);
    return false;
  }
  return true;
};

function isTokenExpired(token: string): boolean {
  try {
    const decodedToken = JSON.parse(atob(token.split('.')[1]));
    const exp = decodedToken.exp;
    const currentTime = Math.floor(Date.now() / 1000);
    return exp < currentTime;
  } catch (error) {
    console.error('Error decoding token:', error);
    return true; // Treat as expired if there's an error
  }
} 
