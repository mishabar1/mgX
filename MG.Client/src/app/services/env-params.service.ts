import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class EnvironmentParamsService {
  private envKey = '__env';

  constructor() {}

  getAPIServerURL() {
    return this.getVal('APIServerURL',  'https://localhost:7114');
    // return this.getVal('APIServerURL', "https://mishabar.com/mgx");
  }

  getVal(key: string, defaultVal: string = ''): string {
    const tokenVal = '${'+key+'}';
    const browserWindow: Record<string, any> = window || {};

    const browserWindowEnv: Record<string, string> =
      (browserWindow[this.envKey as any] as unknown as Record<string, string>) || ({} as Record<string, string>);

    let val: string = defaultVal;

    if (browserWindowEnv.hasOwnProperty(key)) {
      val = (browserWindowEnv as any)[key as any];
    }
    if (val === tokenVal) {
      console.log(`for ${key} we set ${defaultVal}`);
      val = defaultVal;
    }

    return val;
  }
}
