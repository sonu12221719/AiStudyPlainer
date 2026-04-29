import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'splitSubjects',
  standalone: true
})
export class SplitSubjectsPipe implements PipeTransform {
  transform(value: string): string[] {
    return value.split(',').map(s => s.trim()).filter(s => s.length > 0);
  }
}
