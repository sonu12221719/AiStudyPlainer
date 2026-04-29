import { Routes } from '@angular/router';
import { Login } from './features/auth/login/login';
import { Signup } from './features/auth/signup/signup';
import { Dashboard } from './features/dashboard/dashboard';
import { authGuard } from './core/guards/auth-guard';
import { AiTutorComponent } from './features/ai-tutor.component/ai-tutor.component';
import { Layout } from './layout/layout';
import { ScheduleComponent } from './features/schedule.component/schedule.component';
import { StudyPlanComponent } from './features/study-plan.component/study-plan.component';

export const routes: Routes = [
    {path: '', redirectTo: 'login', pathMatch: 'full'},
    {path: 'login', component: Login},
    {path: 'signup', component: Signup},
    {
        path:"",
        component: Layout,
        canActivate: [authGuard],
        children:[
            {path: 'dashboard', component: Dashboard},
            {path: 'study-plan', component: StudyPlanComponent},
            {path: 'ai-tutor', component: AiTutorComponent},
            {path: 'schedule', component: ScheduleComponent}
        ]
    }
];
