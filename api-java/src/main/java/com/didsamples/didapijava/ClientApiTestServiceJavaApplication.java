package com.didsamples.clientapitestservicejava;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.cache.annotation.EnableCaching;

@SpringBootApplication
@EnableCaching
public class ClientApiTestServiceJavaApplication {

	public static void main(String[] args) {
		SpringApplication.run(ClientApiTestServiceJavaApplication.class, args);
	}

}
