import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FlashcardsComponent } from './flashcards.component';

describe('FlashcardsComponent', () => {
  let component: FlashcardsComponent;
  let fixture: ComponentFixture<FlashcardsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FlashcardsComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(FlashcardsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
