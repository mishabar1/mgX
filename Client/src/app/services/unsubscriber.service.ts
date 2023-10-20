import {Injectable, OnDestroy} from '@angular/core';
import {Observable, Subject, takeUntil} from 'rxjs';

@Injectable()
export class UnsubscriberService implements OnDestroy {
  private readonly destroy$ = new Subject<void>();

  public readonly takeUntilDestroy = <T>(origin: Observable<T>): Observable<T> => origin.pipe(takeUntil(this.destroy$));

  public ngOnDestroy(): void {
    console.log('UnsubscriberService', 'ngOnDestroy');
    this.destroy$.next();
    this.destroy$.complete();
  }
}
