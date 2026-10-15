#include <stdint.h>
#include <stdbool.h>
#include "inc/hw_memmap.h"
#include "driverlib/sysctl.h"
#include "driverlib/gpio.h"

/* EK-TM4C123GXL: PF1=kirmizi, PF2=mavi, PF3=yesil. */
volatile uint32_t renk_sayaci = 0;
int main(void)
{
    const uint8_t ledler = GPIO_PIN_1 | GPIO_PIN_2 | GPIO_PIN_3;
    SysCtlClockSet(SYSCTL_SYSDIV_2_5 | SYSCTL_USE_PLL |
                   SYSCTL_OSC_MAIN | SYSCTL_XTAL_16MHZ);
    SysCtlPeripheralEnable(SYSCTL_PERIPH_GPIOF);
    while (!SysCtlPeripheralReady(SYSCTL_PERIPH_GPIOF)) {}
    GPIOPinTypeGPIOOutput(GPIO_PORTF_BASE, ledler);

    while (1)
    {
        GPIOPinWrite(GPIO_PORTF_BASE, ledler, GPIO_PIN_1);
        renk_sayaci++;
        SysCtlDelay(SysCtlClockGet() / 12);
        GPIOPinWrite(GPIO_PORTF_BASE, ledler, GPIO_PIN_3);
        renk_sayaci++;
        SysCtlDelay(SysCtlClockGet() / 12);
        GPIOPinWrite(GPIO_PORTF_BASE, ledler, GPIO_PIN_2);
        renk_sayaci++;
        SysCtlDelay(SysCtlClockGet() / 12);
    }
}
