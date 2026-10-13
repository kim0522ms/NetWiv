#include <ApplicationServices/ApplicationServices.h>
#include <stdio.h>
int main(void) {
  CGDirectDisplayID ids[32]; uint32_t count=0;
  CGError error=CGGetActiveDisplayList(32,ids,&count);
  printf("active error=%d count=%u\n",error,count);
  for(uint32_t i=0;i<count;i++) {
    CGRect b=CGDisplayBounds(ids[i]);
    printf("id=%u online=%d active=%d asleep=%d pixels=%zux%zu bounds=%.0f,%.0f %.0fx%.0f\n",ids[i],CGDisplayIsOnline(ids[i]),CGDisplayIsActive(ids[i]),CGDisplayIsAsleep(ids[i]),CGDisplayPixelsWide(ids[i]),CGDisplayPixelsHigh(ids[i]),b.origin.x,b.origin.y,b.size.width,b.size.height);
  }
  count=0; error=CGGetOnlineDisplayList(32,ids,&count);
  printf("online error=%d count=%u\n",error,count);
  printf("main=%u\n",CGMainDisplayID());
  return 0;
}
